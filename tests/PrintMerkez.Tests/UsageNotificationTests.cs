using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;
namespace PrintMerkez.Tests;

public class UsageNotificationTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter()}};
    private static HttpClient Client(AppFactory app)
    {
        var client=app.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("X-Agent-Key",AppFactory.Key);return client;
    }
    private static async Task<(int User,int Manager,int Policy,int Department)> Setup(PrintDbContext db,int threshold=10,bool assignment=true,EventSource source=EventSource.WindowsPrintService)
    {
        var department=new Department{Name="Bildirim departmanı"};db.Departments.Add(department);await db.SaveChangesAsync();
        var user=new DirectoryUser{Account="DOMAIN\\employee",NormalizedAccount=DirectoryUser.Normalize("DOMAIN\\employee"),DisplayName="Çalışan",DepartmentId=department.Id,LocationId=1};
        var manager=new DirectoryUser{Account="DOMAIN\\manager",NormalizedAccount=DirectoryUser.Normalize("DOMAIN\\manager"),DisplayName="Yönetici",DepartmentId=department.Id,LocationId=1,Role=OrganizationRole.Manager,Email="manager@example.invalid"};
        var policy=new UsageAlertPolicy{Name="Aylık test kuralı",Threshold=threshold,Period=UsagePeriod.Monthly,Source=source};
        db.DirectoryUsers.AddRange(user,manager);db.UsageAlertPolicies.Add(policy);await db.SaveChangesAsync();
        if(assignment){db.ManagerAssignments.Add(new ManagerAssignment{DepartmentId=department.Id,LocationId=1,ManagerUserId=manager.Id});await db.SaveChangesAsync();}
        return(user.Id,manager.Id,policy.Id,department.Id);
    }
    private static PrintEventRequest Event(int pages,DateTimeOffset? time=null,EventSource source=EventSource.WindowsPrintService)=>new()
    {
        EventId=Guid.NewGuid().ToString("N"),Server="SRV-PRINT",PrinterId=1,Account=" domain\\EMPLOYEE ",OccurredAt=time??DateTimeOffset.UtcNow,
        Impressions=pages,Copies=4,Color=PrintColor.Unknown,Status=PrintJobStatus.QueueCompleted,Source=source
    };
    private static async Task<UsageNotification> Row(AppFactory app)
    {
        using var scope=app.Services.CreateScope();return await scope.ServiceProvider.GetRequiredService<PrintDbContext>().UsageNotifications.AsNoTracking().SingleAsync();
    }
    private sealed class FakeSender(params NotificationState[] results):IUsageMailSender
    {
        public int Calls{get;private set;}
        public string? LastRecipient{get;private set;}
        public Task<DeliveryResult> SendAsync(UsageNotification n,CancellationToken ct)
        {
            LastRecipient=n.RecipientEmail;var state=results.Length==0?NotificationState.Sent:results[Math.Min(Calls,results.Length-1)];Calls++;return Task.FromResult(new DeliveryResult(state,"test"));
        }
    }
    [Fact]
    public async Task StrictMonthlyThresholdDeduplicatesAndDoesNotMultiplyCopies()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>());
        var a=Event(6);var b=Event(4);var c=Event(1);
        foreach(var e in new[]{a,b})Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/print-events",e,Json)).StatusCode);
        using(var scope=app.Services.CreateScope())Assert.Empty(await scope.ServiceProvider.GetRequiredService<PrintDbContext>().UsageNotifications.ToListAsync());
        Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/print-events",c,Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/print-events",c,Json)).StatusCode);
        await client.PostAsJsonAsync("/api/v1/print-events",Event(20),Json);
        var notification=await Row(app);Assert.Equal(11,notification.TotalImpressions);Assert.Equal(10,notification.Threshold);
        Assert.Equal("manager@example.invalid",notification.RecipientEmail);Assert.Equal(NotificationState.Pending,notification.State);
    }
    [Fact]
    public async Task SimulationCancelledFailedAndUnknownCountsDoNotContaminateRealTotal()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>());
        var cancelled=Event(100);cancelled.Status=PrintJobStatus.Cancelled;var failed=Event(100);failed.Status=PrintJobStatus.Failed;var unknown=Event(100);unknown.Impressions=null;
        foreach(var e in new[]{Event(100,source:EventSource.Simulation),cancelled,failed,unknown,Event(10)})await client.PostAsJsonAsync("/api/v1/print-events",e,Json);
        using(var scope=app.Services.CreateScope())Assert.Empty(await scope.ServiceProvider.GetRequiredService<PrintDbContext>().UsageNotifications.ToListAsync());
        await client.PostAsJsonAsync("/api/v1/print-events",Event(1),Json);Assert.Equal(11,(await Row(app)).TotalImpressions);
    }
    [Fact]
    public async Task IstanbulMonthBoundarySeparatesUtcDatesAndAllowsOneAlertPerMonth()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>(),5);
        var month=new DateTimeOffset(DateTimeOffset.UtcNow.Year,DateTimeOffset.UtcNow.Month,1,0,0,0,TimeSpan.FromHours(3));
        await client.PostAsJsonAsync("/api/v1/print-events",Event(6,month.AddSeconds(-1)),Json);
        await client.PostAsJsonAsync("/api/v1/print-events",Event(6,month),Json);
        using var scope2=app.Services.CreateScope();var rows=await scope2.ServiceProvider.GetRequiredService<PrintDbContext>().UsageNotifications.ToListAsync();
        Assert.Equal(2,rows.Count);Assert.Equal(2,rows.Select(n=>n.PeriodKey).Distinct().Count());Assert.All(rows,n=>Assert.Equal(6,n.TotalImpressions));
        var window=UsageWindow.For(month.UtcDateTime,UsagePeriod.Monthly);Assert.Equal(month.UtcDateTime,window.StartUtc);
    }
    [Fact]
    public async Task ConcurrentEventsProduceOneAlertAndCorrectAccumulation()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>(),10);
        var responses=await Task.WhenAll(client.PostAsJsonAsync("/api/v1/print-events",Event(6),Json),client.PostAsJsonAsync("/api/v1/print-events",Event(6),Json));
        Assert.All(responses,r=>Assert.Equal(HttpStatusCode.Created,r.StatusCode));Assert.Equal(12,(await Row(app)).TotalImpressions);
    }
    [Fact]
    public async Task MissingManagerBlocksThenRecoversWithoutLosingAlert()
    {
        using var app=new AppFactory();using var client=Client(app);(int User,int Manager,int Policy,int Department) ids;
        using(var scope=app.Services.CreateScope())ids=await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>(),10,false);
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);Assert.Equal(NotificationState.Blocked,(await Row(app)).State);
        var sender=new FakeSender();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var processor=new NotificationProcessor(db,sender);await processor.ProcessBatchAsync();Assert.Equal(0,sender.Calls);
            db.ManagerAssignments.Add(new ManagerAssignment{DepartmentId=ids.Department,LocationId=1,ManagerUserId=ids.Manager});await db.SaveChangesAsync();
            await db.UsageNotifications.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.NextAttemptAtUtc,DateTime.UtcNow));await processor.ProcessBatchAsync();
        }
        Assert.Equal(1,sender.Calls);Assert.Equal(NotificationState.Sent,(await Row(app)).State);
    }
    [Theory]
    [InlineData("inactive")][InlineData("wrong-location")][InlineData("missing-email")][InlineData("wrong-role")][InlineData("self")]
    public async Task InvalidManagerNeverReceivesMail(string problem)
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var ids=await Setup(db);
            var manager=(await db.DirectoryUsers.FindAsync(ids.Manager))!;
            if(problem=="inactive")manager.IsActive=false;if(problem=="wrong-location")manager.LocationId=2;if(problem=="missing-email")manager.Email=null;if(problem=="wrong-role")manager.Role=OrganizationRole.Employee;
            if(problem=="self") { var user=(await db.DirectoryUsers.FindAsync(ids.User))!;user.Role=OrganizationRole.Manager;user.Email="self@example.invalid";(await db.ManagerAssignments.SingleAsync()).ManagerUserId=user.Id; }
            await db.SaveChangesAsync();
        }
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);var sender=new FakeSender();
        using(var scope=app.Services.CreateScope())await new NotificationProcessor(scope.ServiceProvider.GetRequiredService<PrintDbContext>(),sender).ProcessBatchAsync();
        Assert.Equal(0,sender.Calls);Assert.Equal(NotificationState.Blocked,(await Row(app)).State);
    }
    [Fact]
    public async Task UnknownEmploymentLocationDoesNotFallBackToPrinterLocation()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var ids=await Setup(db);(await db.DirectoryUsers.FindAsync(ids.User))!.LocationId=null;await db.SaveChangesAsync();}
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);Assert.Equal(NotificationState.Blocked,(await Row(app)).State);Assert.Null((await Row(app)).RecipientEmail);
    }
    [Fact]
    public async Task OrganizationMoveAfterQueueDoesNotSendToWrongScope()
    {
        using var app=new AppFactory();using var client=Client(app);int userId;
        using(var scope=app.Services.CreateScope())userId=(await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>())).User;
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);
        var sender=new FakeSender();using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();(await db.DirectoryUsers.FindAsync(userId))!.LocationId=2;await db.SaveChangesAsync();await new NotificationProcessor(db,sender).ProcessBatchAsync();}
        Assert.Equal(0,sender.Calls);Assert.Equal(NotificationState.Blocked,(await Row(app)).State);
    }
    [Fact]
    public async Task RecipientIsRevalidatedBeforeDeliveryAndSentRowsDoNotRepeat()
    {
        using var app=new AppFactory();using var client=Client(app);int managerId;
        using(var scope=app.Services.CreateScope())managerId=(await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>())).Manager;
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);var sender=new FakeSender();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();(await db.DirectoryUsers.FindAsync(managerId))!.Email="new@example.invalid";await db.SaveChangesAsync();
            var processor=new NotificationProcessor(db,sender);await processor.ProcessBatchAsync();await processor.ProcessBatchAsync();
        }
        Assert.Equal("new@example.invalid",sender.LastRecipient);Assert.Equal(1,sender.Calls);Assert.Equal(NotificationState.Sent,(await Row(app)).State);
    }
    [Fact]
    public async Task SafeRetriesStopAfterFiveAttemptsAndUncertainIsNeverAutoRetried()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>());
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);var sender=new FakeSender(NotificationState.Retry);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var processor=new NotificationProcessor(db,sender);
            for(var i=0;i<6;i++){await db.UsageNotifications.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.NextAttemptAtUtc,DateTime.UtcNow));await processor.ProcessBatchAsync();}
        }
        Assert.Equal(5,sender.Calls);Assert.Equal(NotificationState.Failed,(await Row(app)).State);
    }
    [Fact]
    public async Task UncertainSendAndInterruptedClaimRequireReviewRatherThanDuplicateDelivery()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>());
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);var sender=new FakeSender(NotificationState.Uncertain);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();var processor=new NotificationProcessor(db,sender);await processor.ProcessBatchAsync();await processor.ProcessBatchAsync();
            Assert.Equal(1,sender.Calls);
            await db.UsageNotifications.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.State,NotificationState.Sending).SetProperty(n=>n.LastAttemptAtUtc,DateTime.UtcNow.AddMinutes(-6)));
            await processor.ProcessBatchAsync();
        }
        Assert.Equal(1,sender.Calls);Assert.Equal(NotificationState.Uncertain,(await Row(app)).State);
    }
    [Fact]
    public async Task SimulationIsPreviewOnlyEvenWithSmtpEnabledAndMisconfigurationIsContained()
    {
        var sender=new UsageMailSender(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Mail:Mode"]="Smtp"}).Build());
        var simulation=await sender.SendAsync(new UsageNotification{Source=EventSource.Simulation},CancellationToken.None);Assert.Equal(NotificationState.Previewed,simulation.State);
        var real=await sender.SendAsync(new UsageNotification{Source=EventSource.WindowsPrintService},CancellationToken.None);Assert.Equal(NotificationState.Failed,real.State);
        var preview=new UsageMailSender(new ConfigurationBuilder().Build());Assert.Equal(NotificationState.Previewed,(await preview.SendAsync(new UsageNotification{Source=EventSource.WindowsPrintService},CancellationToken.None)).State);
    }
    [Fact]
    public async Task DisabledRuleCancelsQueuedMail()
    {
        using var app=new AppFactory();using var client=Client(app);int policyId;
        using(var scope=app.Services.CreateScope())policyId=(await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>())).Policy;
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);var sender=new FakeSender();
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();(await db.UsageAlertPolicies.FindAsync(policyId))!.IsActive=false;await db.SaveChangesAsync();await new NotificationProcessor(db,sender).ProcessBatchAsync();}
        Assert.Equal(0,sender.Calls);Assert.Equal(NotificationState.Cancelled,(await Row(app)).State);
    }
    [Fact]
    public async Task FailedOutboxInsertRollsBackJobSoAgentCanRetry()
    {
        using var app=new AppFactory();using var client=Client(app);var request=Event(11);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();await Setup(db);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER outbox_failure BEFORE INSERT ON UsageNotifications BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            await Assert.ThrowsAsync<DbUpdateException>(()=>EventIngestion.AcceptAsync(request,db));
        }
        using var verify=app.Services.CreateScope();var check=verify.ServiceProvider.GetRequiredService<PrintDbContext>();
        Assert.False(await check.Jobs.AnyAsync(j=>j.EventId==request.EventId));Assert.Empty(await check.UsageNotifications.ToListAsync());
    }
    [Fact]
    public async Task ReconcileCanMatchEarlierUnknownAccountAndDoesNotDuplicate()
    {
        using var app=new AppFactory();using var client=Client(app);await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();await Setup(db);await UsageAlertService.ReconcileCurrentAsync(db);await UsageAlertService.ReconcileCurrentAsync(db);
            Assert.Single(await db.UsageNotifications.ToListAsync());
        }
    }

    private static async Task<Dictionary<string,string>> Form(HttpClient client,string url,Dictionary<string,string> data)
    {
        var html=await client.GetStringAsync(url);
        data["__RequestVerificationToken"]=WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(data["__RequestVerificationToken"]);return data;
    }
    [Theory]
    [InlineData("/Managers")][InlineData("/Notifications")][InlineData("/Notifications/Policies")]
    public async Task NotificationPagesRender(string url)
    {
        using var app=new AppFactory();using var client=Client(app);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(url)).StatusCode);
    }
    [Fact]
    public async Task ManagerRoleNeedsEmailAndAssignmentRejectsWrongOrganization()
    {
        using var app=new AppFactory();using var client=Client(app);
        var invalid=await Form(client,"/Users/Edit",new(){["Input.Account"]="DOMAIN\\badmanager",["Input.DisplayName"]="Eksik yönetici",["Input.Role"]="Manager"});
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Users/Edit",new FormUrlEncodedContent(invalid))).StatusCode);
        (int User,int Manager,int Policy,int Department) ids;
        using(var scope=app.Services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();Assert.False(await db.DirectoryUsers.AnyAsync(u=>u.Account=="DOMAIN\\badmanager"));ids=await Setup(db,10,false);}
        var wrong=await Form(client,"/Managers",new(){["Input.DepartmentId"]=ids.Department.ToString(),["Input.LocationId"]="2",["Input.ManagerUserId"]=ids.Manager.ToString(),["Input.IsActive"]="true"});
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Managers",new FormUrlEncodedContent(wrong))).StatusCode);
        wrong["Input.LocationId"]="1";Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Managers",new FormUrlEncodedContent(wrong))).StatusCode);
        using var verify=app.Services.CreateScope();Assert.Single(await verify.ServiceProvider.GetRequiredService<PrintDbContext>().ManagerAssignments.ToListAsync());
    }
    [Fact]
    public async Task PolicyAndQueueActionsRequireCsrfAndPreserveMonthlyDeduplication()
    {
        using var app=new AppFactory();using var client=Client(app);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Notifications?handler=Evaluate",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        var fields=await Form(client,"/Notifications/Policies",new(){["Input.Name"]="Form kuralı",["Input.Threshold"]="0",["Input.Source"]="WindowsPrintService",["Input.IsActive"]="true"});
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Notifications/Policies",new FormUrlEncodedContent(fields))).StatusCode);
        fields["Input.Threshold"]="50";Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Notifications/Policies",new FormUrlEncodedContent(fields))).StatusCode);
        using var scope=app.Services.CreateScope();var rule=await scope.ServiceProvider.GetRequiredService<PrintDbContext>().UsageAlertPolicies.SingleAsync();Assert.Equal(UsagePeriod.Monthly,rule.Period);Assert.Equal(50,rule.Threshold);
    }
    [Fact]
    public async Task DemoNotificationsAreSyntheticAndRepeatSafe()
    {
        using var app=new AppFactory();using var client=Client(app);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        await ScenarioSeed.InitializeAsync(db);await NotificationDemoSeed.InitializeAsync(db);
        var rows=await db.UsageNotifications.AsNoTracking().ToListAsync();Assert.NotEmpty(rows);Assert.All(rows,n=>Assert.Equal(EventSource.Simulation,n.Source));
        Assert.All(await db.DirectoryUsers.Where(u=>u.Role==OrganizationRole.Manager).ToListAsync(),u=>Assert.EndsWith("@example.invalid",u.Email));
        var counts=new[]{await db.Jobs.CountAsync(),await db.DirectoryUsers.CountAsync(),await db.ManagerAssignments.CountAsync(),await db.UsageNotifications.CountAsync()};
        await NotificationDemoSeed.InitializeAsync(db);
        Assert.Equal(counts,new[]{await db.Jobs.CountAsync(),await db.DirectoryUsers.CountAsync(),await db.ManagerAssignments.CountAsync(),await db.UsageNotifications.CountAsync()});
        var sender=new UsageMailSender(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Mail:Mode"]="Smtp"}).Build());
        for(var i=0;i<4;i++)await new NotificationProcessor(db,sender).ProcessBatchAsync();
        // An inactive example may cross the monthly threshold through historical scenario jobs.
        // Such notifications must remain blocked, rather than being sent or previewed as deliverable.
        var activeUsers = await db.DirectoryUsers.ToDictionaryAsync(u => u.Id, u => u.IsActive);
        Assert.All(await db.UsageNotifications.AsNoTracking().ToListAsync(),n=>
            Assert.Equal(activeUsers[n.UserId] ? NotificationState.Previewed : NotificationState.Blocked,n.State));
    }
    private sealed class BlockingSender:IUsageMailSender
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async Task<DeliveryResult> SendAsync(UsageNotification n,CancellationToken ct){Calls++;Started.SetResult();await Release.Task;return new(NotificationState.Sent);}
    }
    [Fact]
    public async Task TwoProcessorsCannotDeliverSameClaimConcurrently()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var setup=app.Services.CreateScope())await Setup(setup.ServiceProvider.GetRequiredService<PrintDbContext>());
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);
        using var first=app.Services.CreateScope();using var second=app.Services.CreateScope();var sender=new BlockingSender();
        var firstTask=new NotificationProcessor(first.ServiceProvider.GetRequiredService<PrintDbContext>(),sender).ProcessBatchAsync();
        await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try {Assert.Equal(0,await new NotificationProcessor(second.ServiceProvider.GetRequiredService<PrintDbContext>(),sender).ProcessBatchAsync());}
        finally {sender.Release.TrySetResult();}
        await firstTask;Assert.Equal(1,sender.Calls);Assert.Equal(NotificationState.Sent,(await Row(app)).State);
    }

    [Fact]
    public async Task CancelAndRetryActionsAreAuditedAndUncertainCannotBeRetried()
    {
        using var app=new AppFactory();using var client=Client(app);
        using(var scope=app.Services.CreateScope())await Setup(scope.ServiceProvider.GetRequiredService<PrintDbContext>());
        await client.PostAsJsonAsync("/api/v1/print-events",Event(11),Json);var row=await Row(app);
        var form=await Form(client,"/Notifications",new());
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync($"/Notifications?handler=Cancel&id={row.Id}",new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(NotificationState.Cancelled,(await Row(app)).State);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync($"/Notifications?handler=Retry&id={row.Id}",new FormUrlEncodedContent(form))).StatusCode);
        using(var scope=app.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<PrintDbContext>().UsageNotifications.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.State,NotificationState.Failed));
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync($"/Notifications?handler=Retry&id={row.Id}",new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(NotificationState.Retry,(await Row(app)).State);
        using(var scope=app.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<PrintDbContext>().UsageNotifications.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.State,NotificationState.Uncertain));
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync($"/Notifications?handler=Retry&id={row.Id}",new FormUrlEncodedContent(form))).StatusCode);
        using var verify=app.Services.CreateScope();Assert.Equal(2,await verify.ServiceProvider.GetRequiredService<PrintDbContext>().AuditEntries.CountAsync(a=>a.Entity=="UsageNotification"));
    }
    [Fact]
    public async Task SmtpWithoutStartTlsNeverFallsBackToUnencryptedDelivery()
    {
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var commands=new List<string>();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var server=Task.Run(async()=>
            {
                using var socket=await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream=socket.GetStream();using var reader=new StreamReader(stream);await using var writer=new StreamWriter(stream){AutoFlush=true,NewLine="\r\n"};
                await writer.WriteLineAsync("220 localhost SMTP test");var command=await reader.ReadLineAsync(timeout.Token);if(command is not null)commands.Add(command);
                await writer.WriteLineAsync("250-localhost");await writer.WriteLineAsync("250 OK");
                while((command=await reader.ReadLineAsync(timeout.Token)) is not null)commands.Add(command);
            },timeout.Token);
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Mail:Mode"]="Smtp",["Mail:Host"]="127.0.0.1",["Mail:Port"]=((IPEndPoint)listener.LocalEndpoint).Port.ToString(),["Mail:From"]="printer@example.test",["Mail:Security"]="StartTls" }).Build();
            var result=await new UsageMailSender(config).SendAsync(new UsageNotification{Source=EventSource.WindowsPrintService,RecipientEmail="manager@example.test",Subject="TLS test",Body="Test"},timeout.Token);
            await server;Assert.Equal(NotificationState.Retry,result.State);Assert.DoesNotContain(commands,c=>c.StartsWith("MAIL FROM",StringComparison.OrdinalIgnoreCase));
        }
        finally{listener.Stop();}
    }
}
