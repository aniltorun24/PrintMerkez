namespace PrintMerkez.Web.Services;

// Optional local-development control channel. Disabled unless the supervisor supplies a stop file.
public sealed class BackgroundStopMonitor(IConfiguration config, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var path = config["Background:StopFile"];
        if (string.IsNullOrWhiteSpace(path)) return;
        while (!ct.IsCancellationRequested)
        {
            if (File.Exists(path)) { lifetime.StopApplication(); return; }
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }
}
