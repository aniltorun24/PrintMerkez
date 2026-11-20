using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Tests;

public sealed class ServerDeploymentTests
{
    private static void DeleteTestDirectory(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "printmerkez-"), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(resolved)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test klasörü dışında silme yapılmadı.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    private static IConfiguration Config(params (string Key, string Value)[] items) => new ConfigurationBuilder()
        .AddInMemoryCollection(items.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();

    [Theory]
    [InlineData("MOI-PRINTSRV",true)]
    [InlineData("Moi-PrintSrv.trnavm.com",true)]
    [InlineData("Moi-PrintSrv.trnavm.com.",true)]
    [InlineData("Moi-PrintSrv.other.example",false)]
    [InlineData("ANOTHER.trnavm.com",false)]
    public void EventLogUsesLocalSessionOnlyForThisComputer(string server, bool expected)
        => Assert.Equal(expected,WindowsPrintReader.IsLocalServer(server,"MOI-PRINTSRV","trnavm.com"));

    [Theory]
    [InlineData("127.0.0.1", false, true)]
    [InlineData("::ffff:127.0.0.1", false, true)]
    [InlineData("10.60.4.20", false, false)]
    [InlineData("10.60.4.20", true, true)]
    [InlineData("192.168.1.20", true, false)]
    public void RemoteAccessNeedsHttpsExplicitOptInAndMatchingNetwork(string address, bool https, bool expected)
    {
        var config = Config(("Deployment:Mode","Server"),("Deployment:Role","Web"),
            ("Deployment:AllowRemote","true"),("Deployment:AllowedNetworks:0","10.60.0.0/16"));
        Assert.Equal(expected, ServerDeployment.Allows(IPAddress.Parse(address), https, config));
    }

    [Fact]
    public void RemoteCollectorAndDevelopmentAccessRemainClosed()
    {
        var remote = IPAddress.Parse("10.60.4.20");
        Assert.False(ServerDeployment.Allows(remote, true, Config()));
        Assert.False(ServerDeployment.Allows(remote, true, Config(("Deployment:Mode","Server"),("Deployment:Role","Collector"),
            ("Deployment:AllowRemote","true"),("Deployment:AllowedNetworks:0","10.60.0.0/16"))));
        Assert.False(ServerDeployment.Allows(remote, true, Config(("Deployment:Mode","Server"),("Deployment:Role","Web"))));
    }

    [Fact]
    public async Task HeartbeatAllowsOnlyOneCollectorAndReaderDetectsStoppedOrStaleWorker()
    {
        var root = Path.Combine(Path.GetTempPath(), "printmerkez-status-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Config(("Deployment:DataDirectory",root));
        var windows = new WindowsCollectorStatus(); var device = new DeviceCollectorStatus();
        windows.Update(new(true,DateTime.UtcNow,null,null,42,null));
        using var heartbeat = new CollectorHeartbeat(config,windows,device);
        using var second = new CollectorHeartbeat(config,windows,device);
        var viewWindows = new WindowsCollectorStatus(); var viewDevice = new DeviceCollectorStatus();
        using var reader = new CollectorStatusReader(config,viewWindows,viewDevice);
        try
        {
            reader.Refresh(); Assert.NotNull(viewWindows.Current.Error);
            await heartbeat.StartAsync(default);
            await Assert.ThrowsAsync<IOException>(() => second.StartAsync(default));
            reader.Refresh(); Assert.Equal(42,viewWindows.Current.Imported); Assert.Null(viewWindows.Current.Error);
            await heartbeat.StopAsync(default);
            reader.Refresh(); Assert.NotNull(viewWindows.Current.Error);
            await File.WriteAllTextAsync(Path.Combine(root,"collector-status.json"),JsonSerializer.Serialize(
                new CollectorHeartbeatState(DateTime.UtcNow.AddMinutes(-2),true,windows.Current,device.Current)));
            reader.Refresh(); Assert.NotNull(viewWindows.Current.Error);
        }
        finally { heartbeat.Dispose(); second.Dispose(); DeleteTestDirectory(root); }
    }

    [Theory]
    [InlineData("Web")]
    [InlineData("Collector")]
    public void ServerRolesRegisterIndependentBackgroundServices(string role)
    {
        var services = new ServiceCollection();
        ServerDeployment.AddRuntimeServices(services, Config(("Deployment:Mode","Server"),("Deployment:Role",role)));
        var names = services.Where(s=>s.ServiceType==typeof(IHostedService)).Select(s=>s.ImplementationType!.Name).ToArray();
        if(role=="Web")
        {
            Assert.Contains(nameof(CollectorStatusReader),names);
            Assert.Contains(nameof(LiveUpdateMonitor),names);
            Assert.DoesNotContain(nameof(WindowsCollector),names);
            Assert.DoesNotContain(nameof(DeviceCopyCollector),names);
            Assert.DoesNotContain(nameof(NotificationWorker),names);
        }
        else
        {
            Assert.Contains(nameof(CollectorHeartbeat),names);
            Assert.Contains(nameof(WindowsCollector),names);
            Assert.Contains(nameof(DeviceCopyCollector),names);
            Assert.Contains(nameof(NotificationWorker),names);
            Assert.DoesNotContain(nameof(CollectorStatusReader),names);
            Assert.DoesNotContain(nameof(LiveUpdateMonitor),names);
        }
        Assert.DoesNotContain(nameof(BackgroundStopMonitor),names);
    }

    [Fact]
    public async Task ServerRejectsDevelopmentPasswordAndSnapshotDoesNotWriteToSource()
    {
        using var app = new AppFactory(); using var scope=app.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var env=scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var config=scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var serverConfig=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>
        { ["Deployment:Mode"]="Server",["Deployment:Role"]="Web",["Deployment:DataDirectory"]=Path.GetTempPath() }).Build();
        Assert.Throws<InvalidOperationException>(()=>ServerDeployment.Validate(serverConfig,env));
        var localTest=new ConfigurationBuilder().AddConfiguration(serverConfig).AddInMemoryCollection(new Dictionary<string,string?>
        { ["Deployment:AllowTestPassword"]="true",["Deployment:AllowRemote"]="false" }).Build();
        ServerDeployment.Validate(localTest,env);
        var remoteTest=new ConfigurationBuilder().AddConfiguration(localTest).AddInMemoryCollection(new Dictionary<string,string?>
        { ["Deployment:AllowRemote"]="true",["Deployment:AllowedNetworks:0"]="10.60.0.0/16" }).Build();
        Assert.Throws<InvalidOperationException>(()=>ServerDeployment.Validate(remoteTest,env));
        Assert.False(ServerDeployment.Allows(IPAddress.Parse("10.60.4.20"),true,remoteTest));
        var root=Path.Combine(Path.GetTempPath(),"printmerkez-export-"+Guid.NewGuid().ToString("N"));
        var before=await db.Jobs.CountAsync();
        try
        {
            await ServerSnapshot.ExportAsync(db,config,env,root);
            using var copy=new PrintDbContext(new DbContextOptionsBuilder<PrintDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root,"printmerkez.db")};Pooling=False").Options);
            Assert.Equal(before,await copy.Jobs.CountAsync()); Assert.Equal(before,await db.Jobs.CountAsync());
            await Assert.ThrowsAsync<IOException>(()=>ServerSnapshot.ExportAsync(db,config,env,root));
        }
        finally { DeleteTestDirectory(root); }
    }

}
