using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Tests;

public sealed class DirectoryImportTests
{
    private static DirectoryExportUser Row(string sam="ad-user",string? department=null,string? office=null,string? dn=null) => new()
    {
        ObjectGuid=Guid.NewGuid().ToString("D"),SamAccountName=sam,Name="Test Person",DisplayName="Test AD User",
        DistinguishedName=dn??$"CN={sam},OU=Bilgi Islem,OU=Users,OU=MOI,DC=trnavm,DC=com",
        Department=department,Office=office,Email="user@example.invalid",Enabled=true,Sid="S-1-5-21-1-2-3-1200"
    };
    private static DirectoryExport Export(params DirectoryExportUser[] rows) => new()
    {DomainDns="trnavm.com",DomainNetBios="TRNAVM",BaseDn="DC=trnavm,DC=com",ExportedBy="TEST",ExportedUtc=DateTime.UtcNow,Users=rows.ToList()};

    [Theory]
    [InlineData("CN=Person\\, Name,OU=Bilgi Islem,OU=Users,OU=MOI,DC=trnavm,DC=com","Bilgi İşlem","Mall of İstanbul")]
    [InlineData("CN=Person,OU=IK,OU=Users,OU=FORUM MERSIN,DC=trnavm,DC=com","İK","Forum Mersin")]
    [InlineData("CN=Person,OU=Users,OU=FORUM KAYSERI,DC=trnavm,DC=com",null,"Forum Kayseri")]
    [InlineData("CN=Person,OU=Kavacık,OU=Users,OU=TRGYO,DC=trnavm,DC=com",null,null)]
    [InlineData("CN=Person,OU=Torun Center Şantiye,OU=Users,OU=TRGYO,DC=trnavm,DC=com",null,"Torun Center")]
    public void OrganizationUsesExplicitLocationAliasesAndDepartmentOu(string dn,string? department,string? location)
    {
        var result=DirectoryOrganization.Resolve(Row(dn:dn));
        Assert.Equal(department,result.Department);Assert.Equal(location,result.Location);Assert.False(result.Conflict);
    }

    [Fact]
    public void EscapedOuAndUtf8DnAreParsedAndConflictingLocationsStayUnknown()
    {
        Assert.Equal("Satın Alma, Lojistik",DirectoryOrganization.OrganizationalUnits(@"CN=A,OU=Satın Alma\, Lojistik,OU=Users,OU=MOI,DC=trnavm,DC=com")[0]);
        Assert.Equal("Bilgi İşlem",DirectoryOrganization.Resolve(Row(dn:@"CN=A,OU=Bilgi \C4\B0\C5\9Flem,OU=Users,OU=MOI,DC=trnavm,DC=com")).Department);
        var conflict=DirectoryOrganization.Resolve(Row(office:"Korupark"));
        Assert.True(conflict.Conflict);Assert.Null(conflict.Location);
        Assert.Null(DirectoryOrganization.Resolve(Row(office:"Hilton Mall Of İstanbul",dn:"CN=A,OU=Users,OU=HILTON MOI,DC=trnavm,DC=com")).Location);
        Assert.Equal("İSG",DirectoryOrganization.Resolve(Row(department:"İSG")).Department);
    }

    [Fact]
    public async Task PreviewDoesNotWriteAndImportIsRepeatableWithoutChangingPrintHistory()
    {
        using var app=new AppFactory();using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var jobs=await db.Jobs.Select(j=>new{j.Id,j.Account,j.DirectoryUserId}).ToArrayAsync();
        var importer=new DirectoryImport(db);var export=Export(Row());
        var beforeUsers=await db.DirectoryUsers.CountAsync();var beforeDepartments=await db.Departments.CountAsync();
        var preview=await importer.RunAsync(export,true);
        Assert.Equal(1,preview.Added);Assert.Equal(beforeUsers,await db.DirectoryUsers.CountAsync());Assert.Equal(beforeDepartments,await db.Departments.CountAsync());
        var first=await importer.RunAsync(export,false);
        Assert.Equal(1,first.Added);var user=await db.DirectoryUsers.SingleAsync(u=>u.AdObjectGuid==export.Users[0].ObjectGuid);
        Assert.Equal(OrganizationRole.Employee,user.Role);Assert.Equal("TRNAVM",user.AdDomain);Assert.Equal("AD-USER",user.AdSamAccountName);
        Assert.Equal("Mall of İstanbul",(await db.Locations.FindAsync(user.LocationId))!.Name);
        var id=user.Id;var again=await importer.RunAsync(export,false);
        Assert.Equal(0,again.Added);Assert.Equal(1,again.Updated);Assert.Equal(id,(await db.DirectoryUsers.SingleAsync(u=>u.AdObjectGuid==user.AdObjectGuid)).Id);
        Assert.Equal(jobs,await db.Jobs.Select(j=>new{j.Id,j.Account,j.DirectoryUserId}).ToArrayAsync());
        Assert.True(await db.AuditEntries.AnyAsync(a=>a.Entity=="DirectoryImport"));
    }

    [Fact]
    public async Task ManualOrganizationAndManagerRoleArePreservedWhileBlankFieldsAreFilled()
    {
        using var app=new AppFactory();using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var department=new Department{Name="Manual Department"};db.Departments.Add(department);await db.SaveChangesAsync();
        var user=new DirectoryUser{Account="TRNAVM\\ad-user",NormalizedAccount="TRNAVM\\AD-USER",DisplayName="Before",
            DepartmentId=department.Id,LocationId=1,Role=OrganizationRole.Manager,Email="manual@example.invalid"};
        db.DirectoryUsers.Add(user);await db.SaveChangesAsync();
        var row=Row(department:"Teknik");row.Email=null;row.Enabled=false;
        var result=await new DirectoryImport(db).RunAsync(Export(row),false);
        Assert.Equal(1,result.Updated);Assert.Equal(0,result.Added);Assert.Equal(department.Id,user.DepartmentId);Assert.Equal(1,user.LocationId);
        Assert.Equal(OrganizationRole.Manager,user.Role);Assert.Equal("manual@example.invalid",user.Email);Assert.False(user.IsActive);
        Assert.Equal("Test AD User",user.DisplayName);Assert.Equal("TRNAVM\\ad-user",user.Account);
    }

    [Fact]
    public async Task SystemAccountsAreSkippedAndAmbiguousManualAccountsAreNotMerged()
    {
        using var app=new AppFactory();using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        db.DirectoryUsers.AddRange(new DirectoryUser{Account="ad-user",NormalizedAccount="AD-USER",DisplayName="A"},
            new DirectoryUser{Account="TRNAVM\\ad-user",NormalizedAccount="TRNAVM\\AD-USER",DisplayName="B"});await db.SaveChangesAsync();
        var builtin=Row("Administrator");builtin.Sid="S-1-5-21-1-2-3-500";
        var system=Row("HealthMailbox000");var user=Row();
        var report=await new DirectoryImport(db).RunAsync(Export(builtin,system,user),false);
        Assert.Equal(2,report.SystemSkipped);Assert.Equal(1,report.Conflicts);Assert.Equal(0,report.Added);Assert.Equal(2,await db.DirectoryUsers.CountAsync());
    }

    [Fact]
    public async Task DuplicateOrForeignSnapshotsFailBeforeAnyUserIsWritten()
    {
        using var app=new AppFactory();using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var row=Row();var duplicate=Export(row,row);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new DirectoryImport(db).RunAsync(duplicate,false));
        var foreign=Export(Row());foreign.DomainDns="other.example";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new DirectoryImport(db).RunAsync(foreign,false));
        Assert.Equal(0,await db.DirectoryUsers.CountAsync());
    }

    [Fact]
    public async Task DomainAliasesMatchButOtherDomainsDoNotAndDailyTotalsIncludeBothForms()
    {
        using var app=new AppFactory();using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        await new DirectoryImport(db).RunAsync(Export(Row()),false);
        var user=await db.DirectoryUsers.SingleAsync();
        Assert.Equal(user.Id,(await DirectoryAccounts.FindAsync("TRNAVM\\ad-user",db))!.Id);
        Assert.Equal(user.Id,(await DirectoryAccounts.FindAsync("ad-user",db))!.Id);
        Assert.Null(await DirectoryAccounts.FindAsync("FOREIGN\\ad-user",db));
        foreach(var account in new[]{"ad-user","TRNAVM\\ad-user","FOREIGN\\ad-user"})
            db.Jobs.Add(new PrintJob{EventId=Guid.NewGuid().ToString(),Server="TEST",PrinterId=1,LocationId=1,
                Account=account,OccurredAtUtc=new DateTime(2026,11,20,10,0,0),Impressions=3,Copies=3,Source=EventSource.WindowsPrintService,Status=PrintJobStatus.QueueCompleted});
        await db.SaveChangesAsync();
        var day=Assert.Single(await new UserDailyUsage(db).ReadAsync(user,new(2026,11,20),new(2026,11,20),EventSource.WindowsPrintService,default));
        Assert.Equal(2,day.Jobs);Assert.Equal(6,day.Impressions);
    }

    [Fact]
    public async Task UserListPaginatesBeyondOldTwoHundredLimit()
    {
        using var app=new AppFactory();using var client=app.CreateClient();using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        for(var i=0;i<211;i++)db.DirectoryUsers.Add(new DirectoryUser{Account=$"PAGE-{i:000}",NormalizedAccount=$"PAGE-{i:000}",DisplayName=$"Page User {i:000}"});
        await db.SaveChangesAsync();
        var first=await client.GetStringAsync("/Users");Assert.Contains("PAGE-000",first);Assert.DoesNotContain("PAGE-210",first);
        var last=await client.GetStringAsync("/Users?PageNumber=5");Assert.Contains("PAGE-210",last);
        Assert.Contains("PAGE-210",await client.GetStringAsync("/Users?Search=PAGE-210"));
    }
}
