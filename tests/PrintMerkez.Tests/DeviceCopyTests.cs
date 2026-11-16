using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Tests;

public sealed class DeviceCopyTests
{
    private static string Detail(int pages = 1, int copies = 3, int? completed = null) => $$"""
        _pp.LastUpdate = '2026/11/13 10:10:00';
        _pp.JobDetail =
        [
        '2',
        'DOCUMENT-TITLE-MUST-NOT-BE-RETAINED',
        'employee',
        _pp.connectedto,
        '2026/11/13 10:07',
        '2026/11/13 10:08',
        '{{pages}}',
        '{{completed ?? copies}}' + '/' + '{{copies}}',
        '{{pages * (completed ?? copies)}}' + '/' + Number('{{pages}}') * Number('{{copies}}'),
        parseInt('0', 10)
        ];
        _pp.JobNumber = '032980';
        _pp.tmpJobStatus[_pp.IndexNum] = parseInt('5', 10);
        _pp.tmpJobStatus[_pp.IndexNum] = parseInt('5', 10);
        """;

    [Theory]
    [InlineData(1,3,3)] [InlineData(2,3,6)] [InlineData(1,1,1)]
    public void ParsesCompletedCopiesAndTotalWithoutRetainingDocumentTitle(int pages, int copies, int total)
    {
        var job = OlivettiJobLog.ParseDetail(Detail(pages,copies), "32980");
        Assert.Equal(pages,job.DocumentPages); Assert.Equal(copies,job.Copies); Assert.Equal(total,job.Impressions);
        Assert.DoesNotContain("DOCUMENT-TITLE",System.Text.Json.JsonSerializer.Serialize(job));
    }

    [Fact]
    public void ParsesActualDeviceColorOverrideAndLeavesAutoOrMixedUnknown()
    {
        Assert.Equal(PrintColor.Monochrome,OlivettiJobLog.ParseDetail(Detail()+"\n_pp.JobDetail[9] = 3;","32980").Color);
        Assert.Equal(PrintColor.Monochrome,OlivettiJobLog.ParseDetail(Detail()+"\n_pp.JobDetail[9] = 2;","32980").Color);
        Assert.Equal(PrintColor.Color,OlivettiJobLog.ParseDetail(Detail()+"\n_pp.JobDetail[9] = 1;","32980").Color);
        Assert.Equal(PrintColor.Unknown,OlivettiJobLog.ParseDetail(Detail(),"32980").Color);
        Assert.Equal(PrintColor.Unknown,OlivettiJobLog.ParseDetail(Detail()+"\n_pp.JobDetail[9] = 8;","32980").Color);
        Assert.Equal(PrintColor.Unknown,OlivettiJobLog.ParseDetail(Detail()+"\n_pp.JobDetail[9] = 3;\n_pp.JobDetail[9] = 1;","32980").Color);
    }

    [Fact]
    public void ReadsRealFirmwareCrLfLineEndings()
    {
        var job=OlivettiJobLog.ParseDetail(Detail().Replace("\r\n","\n").Replace("\n","\r\n"),"32980");
        Assert.Equal(3,job.Copies);Assert.Equal(3,job.Impressions);
    }

    [Fact]
    public void RejectsPartialFailedOrInconsistentJobsAndWrongJobIdentity()
    {
        Assert.Throws<FormatException>(()=>OlivettiJobLog.ParseDetail(Detail(completed:2),"32980"));
        Assert.Throws<FormatException>(()=>OlivettiJobLog.ParseDetail(Detail().Replace("parseInt('5'","parseInt('6'"),"32980"));
        Assert.Throws<FormatException>(()=>OlivettiJobLog.ParseDetail(Detail(),"32981"));
        Assert.Throws<FormatException>(()=>OlivettiJobLog.ParseDetail(Detail().Replace("'3' + '/' + Number", "'99' + '/' + Number"),"32980"));
    }

    [Fact]
    public void ParsesOnlyAllowlistedListFieldsAndEscapedAccount()
    {
        const string list = """
            Index = parseInt('0', 10);
            _pp.JobKey[Index] = '32980';
            _pp.JobUsername[Index] = 'DOMAIN\\employee';
            _pp.JobName[Index] = 'DO NOT RETAIN';
            Index = parseInt('1', 10);
            _pp.JobKey[Index] = '32981';
            _pp.JobUsername[Index] = 'other';
            _pp.totaljobcount = parseInt('2', 10);
            """;
        var parsed=OlivettiJobLog.ParseList(list);
        Assert.Equal(2,parsed.Total); Assert.Equal(2,parsed.Jobs.Count);
        Assert.Equal("DOMAIN\\employee",parsed.Jobs[0].Account);
        Assert.DoesNotContain("DO NOT RETAIN",System.Text.Json.JsonSerializer.Serialize(parsed.Jobs));
        Assert.Throws<FormatException>(()=>OlivettiJobLog.ParseList(list.Replace("'other'", "evil()")));
    }

    private static ObservedDeviceJob Observed(DateTime now)
    {
        DateTime Local(DateTime time)=>DateTime.SpecifyKind(time.AddHours(3),DateTimeKind.Unspecified);
        return new(new("032980","employee",Local(now.AddMinutes(-2)),Local(now.AddMinutes(-1)),Local(now),1,3,3),now);
    }
    private static PrintJob Pending(DateTime now,int id=1)=>new()
    {
        Id=id,Account="DOMAIN\\employee",ReportedDocumentPages=1,OccurredAtUtc=now.AddSeconds(-90),
        Status=PrintJobStatus.QueueCompleted,Source=EventSource.WindowsPrintService
    };

    [Fact]
    public void MatchingHandlesDeviceClockDifferenceAndRejectsAmbiguityOrWrongUser()
    {
        var now=DateTime.UtcNow; var observation=Observed(now);
        // A device clock that is additionally 15 minutes fast must not change the match.
        var skewed=observation with { Job=observation.Job with { StartedLocal=observation.Job.StartedLocal.AddMinutes(15),
            CompletedLocal=observation.Job.CompletedLocal.AddMinutes(15),DeviceNowLocal=observation.Job.DeviceNowLocal.AddMinutes(15) } };
        Assert.Single(DeviceCopyMatcher.Match([Pending(now)],[skewed]));
        Assert.Empty(DeviceCopyMatcher.Match([Pending(now),Pending(now,2)],[observation]));
        Assert.Empty(DeviceCopyMatcher.Match([Pending(now)],[observation,observation with {Job=observation.Job with {Key="032981"}}]));
        Assert.Empty(DeviceCopyMatcher.Match([Pending(now)],[observation with {Job=observation.Job with {Account="other"}}]));
        var different=Pending(now);different.ReportedDocumentPages=2;
        Assert.Empty(DeviceCopyMatcher.Match([different],[observation]));
    }

    private sealed class FakeDevice(ObservedDeviceJob observation) : IDeviceJobLogClient
    {
        public Task<DeviceLogBatch> ReadAsync(IReadOnlySet<string> accounts,CancellationToken ct)=>
            Task.FromResult(new DeviceLogBatch([observation],false,0));
    }

    [Theory]
    [InlineData(PrintColor.Monochrome,45)]
    [InlineData(PrintColor.Color,90)]
    public async Task DeviceColorUsesHistoricalRateAndDoesNotMultiplyCopiesTwice(PrintColor actual,int expectedCost)
    {
        using var app=new AppFactory();using var client=app.CreateClient();var now=DateTime.UtcNow;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            var printer=await db.Printers.FindAsync(1);printer!.IpAddress="192.0.2.20";
            db.Tariffs.Add(new(){PrinterId=1,EffectiveFromUtc=now.AddDays(-1),MonochromeKurus=15,ColorKurus=30});
            db.Tariffs.Add(new(){PrinterId=1,EffectiveFromUtc=now.AddDays(1),MonochromeKurus=50,ColorKurus=100});
            var job=Pending(now);job.Id=0;job.EventId="color-cost";job.Server="PRINT01";job.PrinterId=1;job.LocationId=1;
            // Requested mode is deliberately opposite; the actual device mode selects the rate.
            job.RequestedColor=actual==PrintColor.Color?PrintColor.Monochrome:PrintColor.Color;
            db.Jobs.Add(job);await db.SaveChangesAsync();
        }
        var observed=Observed(now);observed=observed with {Job=observed.Job with {Color=actual}};
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["DeviceCollector:PrinterId"]="1",["DeviceCollector:Url"]="https://192.0.2.20/"}).Build();
        using var collector=new DeviceCopyCollector(new FakeDevice(observed),app.Services.GetRequiredService<IServiceScopeFactory>(),config,new(),NullLogger<DeviceCopyCollector>.Instance);
        Assert.Equal(1,await collector.PollAsync(default));Assert.Equal(0,await collector.PollAsync(default));
        using var read=app.Services.CreateScope();var stored=await read.ServiceProvider.GetRequiredService<PrintDbContext>().Jobs.SingleAsync(j=>j.EventId=="color-cost");
        Assert.Equal(3,stored.Copies);Assert.Equal(3,stored.Impressions);Assert.Equal(actual,stored.Color);
        Assert.Equal(expectedCost,stored.CostKurus);Assert.Equal(CostState.Calculated,stored.CostState);
        Assert.Equal(actual==PrintColor.Color?30:15,stored.AppliedUnitKurus);
        var html=WebUtility.HtmlDecode(await client.GetStringAsync("/Jobs?Search=color-cost"));
        Assert.Contains((expectedCost/100m).ToString("N2",CultureInfo.GetCultureInfo("tr-TR"))+" ₺",html);
        Assert.Contains("Windows kaydı: "+stored.RequestedColor.Display(),html);
        var csv=await client.GetStringAsync("/Jobs?handler=Export&Search=color-cost");
        Assert.Contains("Windows renk talebi",csv);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LateColorCompletesKnownCountWithoutApplyingFutureTariff(bool futureOnly)
    {
        using var app=new AppFactory();using var client=app.CreateClient();var now=DateTime.UtcNow;
        var observed=Observed(now);var key=DeviceCopyMatcher.Key(observed.Job);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            var printer=await db.Printers.FindAsync(1);printer!.IpAddress="192.0.2.20";
            db.Tariffs.Add(new(){PrinterId=1,EffectiveFromUtc=futureOnly?now.AddDays(1):now.AddDays(-1),MonochromeKurus=15,ColorKurus=30});
            var job=Pending(now);job.Id=0;job.EventId="late-color";job.Server="PRINT01";job.PrinterId=1;job.LocationId=1;
            job.Copies=3;job.Impressions=3;job.DeviceJobKey=key;job.CountVerifiedAtUtc=now.AddMinutes(-1);
            db.Jobs.Add(job);await db.SaveChangesAsync();
        }
        observed=observed with {Job=observed.Job with {Color=PrintColor.Monochrome}};
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["DeviceCollector:PrinterId"]="1",["DeviceCollector:Url"]="https://192.0.2.20/"}).Build();
        using var collector=new DeviceCopyCollector(new FakeDevice(observed),app.Services.GetRequiredService<IServiceScopeFactory>(),config,new(),NullLogger<DeviceCopyCollector>.Instance);
        Assert.Equal(1,await collector.PollAsync(default));Assert.Equal(0,await collector.PollAsync(default));
        using var read=app.Services.CreateScope();var stored=await read.ServiceProvider.GetRequiredService<PrintDbContext>().Jobs.SingleAsync(j=>j.EventId=="late-color");
        Assert.Equal(PrintColor.Monochrome,stored.Color);Assert.Equal(key,stored.DeviceJobKey);
        Assert.Equal(futureOnly?CostState.NoTariff:CostState.Calculated,stored.CostState);
        Assert.Equal(futureOnly?(long?)null:45,stored.CostKurus);
    }

    [Fact]
    public async Task ThreeCopiesFeedMonthlyThresholdOnceAndPreserveDirectorySnapshot()
    {
        using var app=new AppFactory();using var client=app.CreateClient();
        var now=DateTime.UtcNow;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            var printer=await db.Printers.FindAsync(1);printer!.IpAddress="192.0.2.20";
            var department=new Department{Name="Real department"};db.Departments.Add(department);await db.SaveChangesAsync();
            var user=new DirectoryUser{Account="DOMAIN\\employee",NormalizedAccount="DOMAIN\\EMPLOYEE",DisplayName="Employee",DepartmentId=department.Id,LocationId=1};
            var manager=new DirectoryUser{Account="DOMAIN\\manager",NormalizedAccount="DOMAIN\\MANAGER",DisplayName="Manager",DepartmentId=department.Id,LocationId=1,Role=OrganizationRole.Manager,Email="manager@example.invalid"};
            db.DirectoryUsers.AddRange(user,manager);await db.SaveChangesAsync();
            db.ManagerAssignments.Add(new(){DepartmentId=department.Id,LocationId=1,ManagerUserId=manager.Id});
            db.UsageAlertPolicies.Add(new(){Name="Real monthly limit",Threshold=3,Source=EventSource.WindowsPrintService});
            var pending=Pending(now);pending.Id=0;pending.EventId="real-copy-job";pending.Server="PRINT01";pending.PrinterId=1;pending.LocationId=1;
            pending.UserDisplayName="Historical name";pending.DepartmentName="Historical department";
            db.Jobs.Add(pending);
            db.Jobs.Add(new(){Account=user.Account,EventId="known-one",Server="PRINT01",PrinterId=1,LocationId=1,OccurredAtUtc=now.AddSeconds(-40),
                Impressions=1,Status=PrintJobStatus.QueueCompleted,Source=EventSource.WindowsPrintService});
            await db.SaveChangesAsync();
        }
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["DeviceCollector:PrinterId"]="1",["DeviceCollector:Url"]="https://192.0.2.20/"}).Build();
        using var collector=new DeviceCopyCollector(new FakeDevice(Observed(now)),app.Services.GetRequiredService<IServiceScopeFactory>(),config,new(),NullLogger<DeviceCopyCollector>.Instance);
        Assert.Equal(1,await collector.PollAsync(default)); Assert.Equal(0,await collector.PollAsync(default));
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            var job=await db.Jobs.SingleAsync(j=>j.EventId=="real-copy-job");
            Assert.Equal(3,job.Copies);Assert.Equal(3,job.Impressions);Assert.Equal(1,job.ReportedDocumentPages);
            Assert.NotNull(job.DeviceJobKey);Assert.NotNull(job.CountVerifiedAtUtc);
            Assert.Equal("Historical name",job.UserDisplayName);Assert.Equal("Historical department",job.DepartmentName);
            var notice=Assert.Single(await db.UsageNotifications.ToListAsync());
            Assert.Equal(4,notice.TotalImpressions);Assert.Equal(NotificationState.Pending,notice.State);
            Assert.Single(await db.AuditEntries.Where(a=>a.Entity=="PrintMeasurement").ToListAsync());
        }
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Jobs"));
        Assert.Contains("Kopya sayısı", html);
        Assert.Contains("Cihaz iş kaydıyla doğrulandı", html);
    }
}
