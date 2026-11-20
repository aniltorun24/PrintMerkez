using System.Net;
using System.Text.Json;

namespace PrintMerkez.Web.Services;

public static class ServerDeployment
{
    public static bool Enabled(IConfiguration config) => config["Deployment:Mode"] == "Server";
    public static bool Collector(IConfiguration config) => config["Deployment:Role"] == "Collector";
    public static void AddRuntimeServices(IServiceCollection services, IConfiguration config)
    {
        var server = Enabled(config); var collector = server && Collector(config);
        if (!collector) services.AddHostedService<LiveUpdateMonitor>();
        if (collector) services.AddHostedService<CollectorHeartbeat>();
        if (!server || collector)
        {
            services.AddHostedService<NotificationWorker>();
            services.AddHostedService<WindowsCollector>();
            services.AddHostedService<DeviceCopyCollector>();
        }
        else services.AddHostedService<CollectorStatusReader>();
        if (!server) services.AddHostedService<BackgroundStopMonitor>();
    }

    public static void Validate(IConfiguration config, IHostEnvironment environment)
    {
        if (!Enabled(config))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Sunucu kurulumu için Deployment:Mode=Server ve ayrı sunucu ayarları gerekli.");
            return;
        }
        if (config["Deployment:Role"] is not ("Web" or "Collector"))
            throw new InvalidOperationException("Sunucu rolü Web veya Collector olmalı.");
        var root = config["Deployment:DataDirectory"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || root.StartsWith(@"\\"))
            throw new InvalidOperationException("Test sunucusu verileri yerel diskte tam yolda tutulmalı.");
        var credentials = new LocalAdminCredentials(config);
        var testPassword = config.GetValue("Deployment:AllowTestPassword", false);
        if (testPassword && config.GetValue("Deployment:AllowRemote", false))
            throw new InvalidOperationException("Test şifresiyle yalnızca sunucunun kendisinden erişim kullanılabilir.");
        if (credentials.Verify(credentials.Username, "12345") && !testPassword)
            throw new InvalidOperationException("Sunucu için geçici geliştirme şifresini değiştirin; Configure-TestServer.ps1 kullanın.");
        foreach (var entry in config.GetSection("Deployment:AllowedNetworks").GetChildren())
            if (!IPNetwork.TryParse(entry.Value, out _)) throw new InvalidOperationException("Geçersiz izinli ağ aralığı.");
    }

    public static bool Allows(IPAddress? remote, bool https, IConfiguration config, bool testing = false)
    {
        if (testing) return true;
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (IPAddress.IsLoopback(remote)) return true;
        if (!Enabled(config) || Collector(config) || !https || config.GetValue("Deployment:AllowTestPassword", false)
            || !config.GetValue("Deployment:AllowRemote", false)) return false;
        return config.GetSection("Deployment:AllowedNetworks").GetChildren()
            .Any(entry => IPNetwork.TryParse(entry.Value, out var network) && network.Contains(remote));
    }
}

public sealed record CollectorHeartbeatState(DateTime WrittenUtc, bool Running,
    CollectorSnapshot Windows, DeviceCollectorSnapshot Device);

// One collector process per local data folder, independent of IIS restarts.
public sealed class CollectorHeartbeat(IConfiguration config, WindowsCollectorStatus windows,
    DeviceCollectorStatus device) : BackgroundService
{
    private FileStream? instanceLock;
    private string PathFor(string name) => Path.Combine(config["Deployment:DataDirectory"]!, name);
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        instanceLock = new FileStream(PathFor("collector-instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await SaveAsync(true, cancellationToken);
        await base.StartAsync(cancellationToken);
    }
    private async Task SaveAsync(bool running, CancellationToken ct)
    {
        var file = PathFor("collector-status.json");
        await File.WriteAllTextAsync(file + ".tmp", JsonSerializer.Serialize(new CollectorHeartbeatState(
            DateTime.UtcNow, running, windows.Current, device.Current)), ct);
        File.Move(file + ".tmp", file, true);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(stoppingToken)) await SaveAsync(true, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await base.StopAsync(cancellationToken); await SaveAsync(false, CancellationToken.None); }
        finally { instanceLock?.Dispose(); }
    }
    public override void Dispose() { instanceLock?.Dispose(); base.Dispose(); }
}

// IIS reads the worker's status file instead of showing its own empty in-memory collector status.
public sealed class CollectorStatusReader(IConfiguration config, WindowsCollectorStatus windows,
    DeviceCollectorStatus device) : BackgroundService
{
    public void Refresh()
    {
        try
        {
            var path = Path.Combine(config["Deployment:DataDirectory"]!, "collector-status.json");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var state = JsonSerializer.Deserialize<CollectorHeartbeatState>(stream);
            if (state is null || !state.Running || DateTime.UtcNow - state.WrittenUtc > TimeSpan.FromSeconds(45)
                || state.WrittenUtc > DateTime.UtcNow.AddMinutes(1)) throw new IOException();
            windows.Update(state.Windows); device.Update(state.Device);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            windows.Update(windows.Current with { Enabled = true, Error = "Toplayıcı servisi çalışmıyor veya durum bilgisi güncel değil." });
            device.Update(device.Current with { Error = "Toplayıcı servisinin güncel durumu alınamadı." });
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Refresh();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(stoppingToken)) Refresh();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
