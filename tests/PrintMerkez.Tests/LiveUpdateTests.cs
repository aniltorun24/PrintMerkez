using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Tests;

public sealed class LiveUpdateTests
{
    [Fact]
    public async Task FragmentsPreserveFiltersAndDoNotReturnLayoutOrForms()
    {
        using var app=new AppFactory();using var client=app.CreateClient();
        var html=await client.GetStringAsync("/Jobs?handler=Live&Source=WindowsPrintService&Search=missing");
        Assert.DoesNotContain("<html",html);Assert.DoesNotContain("<form",html);
        Assert.Contains("0 kayıt",WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("ORNEK",html);
        var dashboard=await client.GetStringAsync("/?handler=Live&Source=Simulation");
        Assert.Contains("Simülasyon görünümü",WebUtility.HtmlDecode(dashboard));
        Assert.DoesNotContain("<form",dashboard);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/Jobs?handler=Live&From=2026-11-20&To=2026-11-01")).StatusCode);
    }

    [Fact]
    public async Task RevisionChangesRefreshDataAndConditionalRequestsRespectFilters()
    {
        using var app=new AppFactory();using var client=app.CreateClient();
        var response=await client.GetAsync("/Jobs?handler=Live&Source=WindowsPrintService");
        var tag=response.Headers.ETag!.ToString();
        using var unchanged=new HttpRequestMessage(HttpMethod.Get,"/Jobs?handler=Live&Source=WindowsPrintService");
        unchanged.Headers.TryAddWithoutValidation("If-None-Match",tag);
        Assert.Equal(HttpStatusCode.NotModified,(await client.SendAsync(unchanged)).StatusCode);
        using var otherFilter=new HttpRequestMessage(HttpMethod.Get,"/Jobs?handler=Live&Source=Simulation");
        otherFilter.Headers.TryAddWithoutValidation("If-None-Match",tag);
        Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(otherFilter)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Agent-Key",AppFactory.Key);
        var job=new PrintEventRequest{EventId="live-update",Server="TEST",PrinterId=1,Account="LIVE-ACCOUNT",OccurredAt=DateTimeOffset.UtcNow,Source=EventSource.WindowsPrintService};
        Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/print-events",job)).StatusCode);
        app.Services.GetRequiredService<LiveUpdates>().Publish();
        var updated=await client.GetStringAsync("/Jobs?handler=Live&Source=WindowsPrintService&v=new");
        Assert.Contains("LIVE-ACCOUNT",updated);
    }

    [Fact]
    public void SlowSubscriberKeepsOnlyLatestRevisionAndUnsubscribeCleansUp()
    {
        var live=new LiveUpdates();var subscription=live.Subscribe();
        for(var i=0;i<10000;i++)live.Publish();
        Assert.True(subscription.Reader.TryRead(out var last));Assert.Equal(live.Version,last);
        Assert.False(subscription.Reader.TryRead(out _));
        live.Remove(subscription.Id);Assert.Equal(0,live.Subscribers);
    }

    [Fact]
    public async Task EventStreamConnectsAndNotifiesWithoutExposingPrintData()
    {
        using var app=new AppFactory();using var client=app.CreateClient();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response=await client.GetAsync("/live/events",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        Assert.Equal("text/event-stream",response.Content.Headers.ContentType!.MediaType);
        using var reader=new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        Assert.Equal("retry: 5000",await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("event: connected",await reader.ReadLineAsync(timeout.Token));
        var connected=await reader.ReadLineAsync(timeout.Token);Assert.StartsWith("data: ",connected);
        Assert.Equal("",await reader.ReadLineAsync(timeout.Token));
        app.Services.GetRequiredService<LiveUpdates>().Publish();
        Assert.Equal("event: changed",await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("data: "+app.Services.GetRequiredService<LiveUpdates>().Version,await reader.ReadLineAsync(timeout.Token));
    }
}
