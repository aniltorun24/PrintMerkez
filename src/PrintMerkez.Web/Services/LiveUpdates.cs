using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Web.Data;
using Microsoft.AspNetCore.Authentication;

namespace PrintMerkez.Web.Services;

public sealed class LiveUpdates
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> clients = new();
    private readonly string generation = Guid.NewGuid().ToString("N");
    private long revision;
    public string Version => generation + "-" + Interlocked.Read(ref revision);
    public int Subscribers => clients.Count;
    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var id = Guid.NewGuid(); clients[id] = channel; return (id, channel.Reader);
    }
    public void Remove(Guid id) { if (clients.TryRemove(id, out var channel)) channel.Writer.TryComplete(); }
    public void Publish()
    {
        Interlocked.Increment(ref revision);
        foreach (var client in clients.Values) client.Writer.TryWrite(Version);
    }
    public static IActionResult? NotModified(PageModel page, LiveUpdates live)
    {
        var filterKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(page.Request.Path + page.Request.QueryString + page.User.Identity?.Name)))[..16];
        var etag = "\"" + live.Version + "-" + filterKey + "\"";
        page.Response.Headers.ETag = etag;
        // Revalidate using the server revision; browsers must not reuse stale fragments on reconnect.
        page.Response.Headers.CacheControl = "no-cache, no-store";
        return page.Request.Headers.IfNoneMatch.ToString() == etag ? new StatusCodeResult(304) : null;
    }
    public static async Task Stream(HttpContext http, LiveUpdates live, IHostApplicationLifetime lifetime)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, lifetime.ApplicationStopping);
        var authentication = await http.AuthenticateAsync();
        if (authentication.Properties?.ExpiresUtc is DateTimeOffset expires)
            linked.CancelAfter(expires > DateTimeOffset.UtcNow ? expires - DateTimeOffset.UtcNow : TimeSpan.Zero);
        var ct = linked.Token; var subscription = live.Subscribe();
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        try
        {
            await http.Response.WriteAsync($"retry: 5000\nevent: connected\ndata: {live.Version}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2)); var ticks = 0;
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (subscription.Reader.TryRead(out var version))
                    await http.Response.WriteAsync($"event: changed\ndata: {version}\n\n", ct);
                else if (++ticks % 7 == 0)
                    await http.Response.WriteAsync("event: ping\ndata: alive\n\n", ct);
                else continue;
                await http.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (IOException) { /* The client can disconnect at any point. */ }
        finally { live.Remove(subscription.Id); }
    }
}

// One database watcher for the whole process, regardless of the number of open browsers.
public sealed class LiveUpdateMonitor(LiveUpdates live, IServiceScopeFactory scopes, IWebHostEnvironment environment,
    WindowsCollectorStatus windows, DeviceCollectorStatus device, ILogger<LiveUpdateMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (environment.IsEnvironment("Testing")) return;
        string? previous = null; var failed = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (live.Subscribers == 0) { previous = null; continue; }
                try
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
                    await db.Database.OpenConnectionAsync(ct);
                    await using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "SELECT (SELECT COALESCE(MAX(Id),0) FROM Jobs), (SELECT COALESCE(MAX(Id),0) FROM AuditEntries)";
                    await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
                    var signature = $"{reader.GetInt64(0)}|{reader.GetInt64(1)}|{windows.Current.Error}|{windows.Current.Warning}|{device.Current.Error}";
                    if (previous != signature || failed) live.Publish();
                    previous = signature; failed = false;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    if (!failed) { logger.LogWarning("Canlı veri kontrolü bekliyor: {Type}", ex.GetType().Name); live.Publish(); }
                    failed = true;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
