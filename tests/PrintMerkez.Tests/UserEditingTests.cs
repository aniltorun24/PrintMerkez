using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Tests;

public sealed class UserEditingTests
{
    private static async Task<int> Person(AppFactory app,bool manager=false,bool ad=false)
    {
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var department=new Department{Name="Edit Test Department"};db.Departments.Add(department);await db.SaveChangesAsync();
        var user=new DirectoryUser{Account="edit-user",NormalizedAccount="EDIT-USER",DisplayName="Original Person",DepartmentId=department.Id,LocationId=1,
            Email="edit@example.invalid",Role=manager?OrganizationRole.Manager:OrganizationRole.Employee};
        if(ad){user.AdObjectGuid=Guid.NewGuid().ToString("D");user.AdDomain="TRNAVM";user.AdSamAccountName="EDIT-USER";}
        db.DirectoryUsers.Add(user);await db.SaveChangesAsync();return user.Id;
    }
    private static string Hidden(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,
        "name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static async Task<Dictionary<string,string>> Form(HttpClient client,int id)
    {
        var html=await client.GetStringAsync($"/Users/Edit/{id}");
        var form=new Dictionary<string,string>{["__RequestVerificationToken"]=Hidden(html,"__RequestVerificationToken"),["ChangeToken"]=Hidden(html,"ChangeToken")};
        Assert.NotEmpty(form["__RequestVerificationToken"]);Assert.NotEmpty(form["ChangeToken"]);return form;
    }
    private static async Task SaveFields(AppFactory app,int id,Dictionary<string,string> form)
    {
        using var scope=app.Services.CreateScope();var user=await scope.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.FindAsync(id);
        form["Input.Account"]=user!.Account;form["Input.DisplayName"]="Changed Person";form["Input.DepartmentId"]=user.DepartmentId!.Value.ToString();
        form["Input.LocationId"]="1";form["Input.Email"]="changed@example.invalid";form["Input.Role"]=user.Role.ToString();form["Input.IsActive"]="true";
    }

    [Fact]
    public async Task ListLinksToSeparateEditPageAndSavePreservesAdIdentity()
    {
        using var app=new AppFactory();using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app,ad:true);
        var list=await client.GetStringAsync("/Users");Assert.Contains($"/Users/Edit/{id}",list);Assert.DoesNotContain("name=\"Input.Account\"",list);
        var old=await client.GetAsync($"/Users/{id}");Assert.Equal(HttpStatusCode.Redirect,old.StatusCode);Assert.Contains($"/Users/Edit/{id}",old.Headers.Location!.ToString());
        var form=await Form(client,id);await SaveFields(app,id,form);form["Search"]="edit-user";form["ListPage"]="3";
        var response=await client.PostAsync($"/Users/Edit/{id}",new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Contains("Search=edit-user",response.Headers.Location!.ToString());
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var user=await db.DirectoryUsers.FindAsync(id);
        Assert.Equal("Changed Person",user!.DisplayName);Assert.NotNull(user.AdObjectGuid);Assert.Equal("TRNAVM",user.AdDomain);Assert.Null(user.DeletedAtUtc);
        Assert.True(await db.AuditEntries.AnyAsync(a=>a.Entity==nameof(DirectoryUser)&&a.EntityKey==id.ToString()&&a.Action=="Modified"));
    }

    [Fact]
    public async Task AnonymousCannotOpenEditOrPostChangesAndDeletion()
    {
        using var app=new AppFactory(false);using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app);
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync($"/Users/Edit/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync($"/Users/Edit/{id}",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync($"/Users/Edit/{id}?handler=Delete",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }

    [Theory]
    [InlineData("Employee")][InlineData("Manager")]
    public async Task AuthenticatedNonAdminIncludingNotificationManagerCannotEditOrDelete(string role)
    {
        using var app=new AppFactory(false);using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app);
        var options=app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity=new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"non-admin"),new Claim(ClaimTypes.Role,role)},CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket=new AuthenticationTicket(new ClaimsPrincipal(identity),new AuthenticationProperties{IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(10)},CookieAuthenticationDefaults.AuthenticationScheme);
        client.DefaultRequestHeaders.Add("Cookie","PrintMerkez.Admin="+options.TicketDataFormat.Protect(ticket));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/Users/Edit/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync($"/Users/Edit/{id}",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync($"/Users/Edit/{id}?handler=Delete",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        using var scope=app.Services.CreateScope();Assert.Null((await scope.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.FindAsync(id))!.DeletedAtUtc);
    }

    [Fact]
    public async Task SaveAndDeleteRequireAntiforgeryAndDeleteNeedsExplicitConfirmation()
    {
        using var app=new AppFactory();using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app);
        var form=await Form(client,id);var noToken=new Dictionary<string,string>(form);noToken.Remove("__RequestVerificationToken");noToken["ConfirmDelete"]="true";
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync($"/Users/Edit/{id}?handler=Delete",new FormUrlEncodedContent(noToken))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync($"/Users/Edit/{id}",new FormUrlEncodedContent(noToken))).StatusCode);
        var notConfirmed=await client.PostAsync($"/Users/Edit/{id}?handler=Delete",new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK,notConfirmed.StatusCode);Assert.Contains("Silme işlemini onaylamalısınız",WebUtility.HtmlDecode(await notConfirmed.Content.ReadAsStringAsync()));
        using var scope=app.Services.CreateScope();Assert.Null((await scope.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.FindAsync(id))!.DeletedAtUtc);
    }

    [Fact]
    public async Task DeletionPreservesHistoryCancelsPendingNotificationsAndDoesNotResurrectOnAdImport()
    {
        using var app=new AppFactory();using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app,ad:true);string guid;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();guid=(await db.DirectoryUsers.FindAsync(id))!.AdObjectGuid!;
            var policy=new UsageAlertPolicy{Name="Deletion policy",Threshold=1};db.UsageAlertPolicies.Add(policy);await db.SaveChangesAsync();
            db.Jobs.Add(new PrintJob{EventId="delete-history",Server="TEST",PrinterId=1,LocationId=1,DirectoryUserId=id,Account="edit-user",UserDisplayName="Historical Name",OccurredAtUtc=DateTime.UtcNow,Impressions=3,Status=PrintJobStatus.QueueCompleted,Source=EventSource.WindowsPrintService});
            db.UsageNotifications.Add(new UsageNotification{PolicyId=policy.Id,UserId=id,PeriodKey="delete-test",State=NotificationState.Pending,Source=EventSource.WindowsPrintService});await db.SaveChangesAsync();
        }
        var form=await Form(client,id);form["ConfirmDelete"]="true";
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync($"/Users/Edit/{id}?handler=Delete",new FormUrlEncodedContent(form))).StatusCode);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var user=(await db.DirectoryUsers.FindAsync(id))!;
            Assert.NotNull(user.DeletedAtUtc);Assert.False(user.IsActive);Assert.Equal("Historical Name",(await db.Jobs.SingleAsync(j=>j.EventId=="delete-history")).UserDisplayName);
            Assert.Equal(NotificationState.Cancelled,(await db.UsageNotifications.SingleAsync()).State);
            Assert.True(await db.AuditEntries.AnyAsync(a=>a.Entity=="DirectoryUserDeletion"&&a.EntityKey==id.ToString()));
            Assert.Null(await DirectoryAccounts.FindAsync("TRNAVM\\edit-user",db));
            var report=await new DirectoryImport(db).RunAsync(new DirectoryExport{DomainDns="trnavm.com",DomainNetBios="TRNAVM",BaseDn="DC=trnavm,DC=com",Users=[new DirectoryExportUser{ObjectGuid=guid,SamAccountName="edit-user",Name="AD user",DisplayName="AD user",Enabled=true,DistinguishedName="CN=Edit,OU=Users,OU=MOI,DC=trnavm,DC=com"}]},false);
            Assert.Equal(1,report.ArchivedSkipped);Assert.False(user.IsActive);Assert.NotNull(user.DeletedAtUtc);
        }
        Assert.DoesNotContain($"/Users/Edit/{id}",await client.GetStringAsync("/Users"));
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/Users/Edit/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/Users/Details/{id}")).StatusCode);
    }

    [Fact]
    public async Task ActiveManagerAssignmentBlocksDeletion()
    {
        using var app=new AppFactory();using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app,manager:true);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var user=(await db.DirectoryUsers.FindAsync(id))!;db.ManagerAssignments.Add(new ManagerAssignment{ManagerUserId=id,DepartmentId=user.DepartmentId!.Value,LocationId=1});await db.SaveChangesAsync();}
        var form=await Form(client,id);form["ConfirmDelete"]="true";var response=await client.PostAsync($"/Users/Edit/{id}?handler=Delete",new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("aktif yönetici atamasında",WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var verify=app.Services.CreateScope();Assert.Null((await verify.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.FindAsync(id))!.DeletedAtUtc);
    }

    [Fact]
    public async Task InFlightNotificationBlocksDeletion()
    {
        using var app=new AppFactory();using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var policy=new UsageAlertPolicy{Name="Sending policy",Threshold=1};db.UsageAlertPolicies.Add(policy);await db.SaveChangesAsync();db.UsageNotifications.Add(new UsageNotification{PolicyId=policy.Id,UserId=id,PeriodKey="sending",State=NotificationState.Sending,Source=EventSource.WindowsPrintService});await db.SaveChangesAsync();}
        var form=await Form(client,id);form["ConfirmDelete"]="true";var response=await client.PostAsync($"/Users/Edit/{id}?handler=Delete",new FormUrlEncodedContent(form));
        Assert.Contains("bildirim işleniyor",WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var verify=app.Services.CreateScope();Assert.Null((await verify.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.FindAsync(id))!.DeletedAtUtc);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task StaleFormsCannotOverwriteOrDeleteNewerUserChanges(bool delete)
    {
        using var app=new AppFactory();using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app);var form=await Form(client,id);
        await SaveFields(app,id,form);form["ConfirmDelete"]="true";
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();(await db.DirectoryUsers.FindAsync(id))!.DisplayName="Another administrator update";await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsync($"/Users/Edit/{id}"+(delete?"?handler=Delete":""),new FormUrlEncodedContent(form))).StatusCode);
        using var verify=app.Services.CreateScope();var user=(await verify.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.FindAsync(id))!;
        Assert.Equal("Another administrator update",user.DisplayName);Assert.Null(user.DeletedAtUtc);
    }

    [Fact]
    public async Task OldPostRouteDoesNotModifyUsersAndMissingEditUserReturns404()
    {
        using var app=new AppFactory();using var client=app.CreateClient(new(){AllowAutoRedirect=false});var id=await Person(app);
        var form=await Form(client,id);await SaveFields(app,id,form);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await client.PostAsync($"/Users/{id}",new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/Users/Edit/2147483647")).StatusCode);
        using var verify=app.Services.CreateScope();Assert.Equal("Original Person",(await verify.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.FindAsync(id))!.DisplayName);
    }
}
