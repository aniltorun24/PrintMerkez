using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Tests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly bool authenticate;
    public AppFactory(bool authenticate = true) => this.authenticate = authenticate;
    public const string Key = "test-agent-secret-32-characters-minimum";
    private readonly string database = Path.Combine(Path.GetTempPath(), $"printmerkez-test-{Guid.NewGuid():N}.db");
    public new HttpClient CreateClient() => AuthenticateClient(base.CreateClient());
    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options) => AuthenticateClient(base.CreateClient(options));
    private HttpClient AuthenticateClient(HttpClient client)
    {
        if (!authenticate) return client;
        var html = client.GetStringAsync("/Login").GetAwaiter().GetResult();
        var token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using var response = client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin", ["Password"] = "12345", ["__RequestVerificationToken"] = token
        })).GetAwaiter().GetResult();
        if (response.StatusCode != HttpStatusCode.Redirect) response.EnsureSuccessStatusCode();
        return client;
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:PrintDb"] = $"Data Source={database};Pooling=False",
            ["Notifications:WorkerEnabled"] = "false",
            ["WindowsCollector:Enabled"] = "false",
            ["DeviceCollector:Enabled"] = "false",
            ["Agent:AllowSimulation"] = "true",
            ["Development:SeedScenarioData"] = "false",
            ["Development:SeedDemoData"] = "true", ["Agent:ApiKey"] = Key
        }));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(database)) File.Delete(database);
    }
}
public sealed class WorkflowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private static PrintEventRequest Event(string? id = null) => new()
    {
        EventId = id ?? Guid.NewGuid().ToString("N"), Server = "SRV-PRINT", PrinterId = 1,
        Account = "TEST\\user", OccurredAt = DateTimeOffset.UtcNow, Impressions = 4, Copies = 2,
        Color = PrintColor.Unknown, Status = PrintJobStatus.QueueCompleted, Source = EventSource.Simulation
    };
    private static HttpClient Client(AppFactory app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Agent-Key", AppFactory.Key);
        return client;
    }
    private static async Task<string> Token(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    [Theory]
    [InlineData("/")][InlineData("/Printers")][InlineData("/Printers/Create")]
    [InlineData("/Locations")][InlineData("/Locations/Edit/1")][InlineData("/Jobs")]
    public async Task PagesRender(string url)
    {
        using var app = new AppFactory(); using var client = Client(app);
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PrintMerkez", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task ApiRejectsMissingKey()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/print-events", Event(), Json);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task DuplicateEventHasOnlyOneDatabaseRecordAndUnknownValuesRemainNull()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var e = Event(); e.Impressions = null; e.Copies = null;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/print-events", e, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/print-events", e, Json)).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var records = await db.Jobs.Where(x => x.EventId == e.EventId).ToListAsync();
        Assert.Single(records); Assert.Null(records[0].Impressions); Assert.Null(records[0].Copies);
        Assert.Equal(PrintColor.Unknown, records[0].Color); Assert.Equal(1, records[0].LocationId);
    }
    [Fact]
    public async Task InvalidCountsAndMissingPrinterAreRejected()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var e = Event(); e.Impressions = -1;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/print-events", e, Json)).StatusCode);
        e.Impressions = 2; e.PrinterId = 9999;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/print-events", e, Json)).StatusCode);
    }
    [Fact]
    public async Task FormsRequireAntiforgeryAndSaveValidPrinter()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var form = new Dictionary<string,string> { ["Input.Name"] = "Yeni cihaz", ["Input.Model"] = "Test model", ["Input.LocationId"] = "1", ["Input.IpAddress"] = "192.0.2.10" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Printers/Create", new FormUrlEncodedContent(form))).StatusCode);
        form["__RequestVerificationToken"] = await Token(client,"/Printers/Create");
        Assert.NotEmpty(form["__RequestVerificationToken"]);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Printers/Create", new FormUrlEncodedContent(form))).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<PrintDbContext>().Printers.AnyAsync(x => x.Name == "Yeni cihaz" && x.IpAddress == "192.0.2.10"));
    }
    [Fact]
    public async Task InvalidIpDoesNotCreatePrinter()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var form = new Dictionary<string,string> { ["Input.Name"] = "Invalid", ["Input.Model"] = "Test", ["Input.LocationId"] = "1", ["Input.IpAddress"] = "not-an-ip", ["__RequestVerificationToken"] = await Token(client,"/Printers/Create") };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Printers/Create", new FormUrlEncodedContent(form))).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PrintDbContext>().Printers.AnyAsync(x => x.Name == "Invalid"));
    }
    [Fact]
    public async Task ExportHonorsFiltersAndProtectsSpreadsheetCells()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var e = Event(); e.Account = "=formula-test";
        await client.PostAsJsonAsync("/api/v1/print-events", e, Json);
        var csv = await client.GetStringAsync("/Jobs?handler=Export&Source=Simulation&Search=formula-test");
        Assert.Contains("'=formula-test", csv); Assert.DoesNotContain("kullanici1", csv);
        var empty = await client.GetStringAsync("/Jobs?handler=Export&Source=WindowsPrintService");
        Assert.DoesNotContain("formula-test", empty);
    }
    [Fact]
    public async Task PrinterMovePreservesHistoricalLocation()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var e = Event(); await client.PostAsJsonAsync("/api/v1/print-events", e, Json);
        var form = new Dictionary<string,string> { ["Input.Name"] = "Pilot Olivetti", ["Input.Model"] = "Olivetti d-COPIA 255MF KX", ["Input.LocationId"] = "2", ["__RequestVerificationToken"] = await Token(client,"/Printers/Create/1") };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Printers/Create/1", new FormUrlEncodedContent(form))).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        Assert.Equal(2, (await db.Printers.FindAsync(1))!.LocationId);
        Assert.Equal(1, (await db.Jobs.SingleAsync(x => x.EventId == e.EventId)).LocationId);
    }

    [Fact]
    public async Task MissingStatusStaysUnknownAndFutureEventIsRejected()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var id = Guid.NewGuid().ToString("N");
        var payload = new { eventId = id, server = "SRV-PRINT", printerId = 1, account = "TEST", occurredAt = DateTimeOffset.UtcNow };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/print-events", payload)).StatusCode);
        using var scope = app.Services.CreateScope();
        var record = await scope.ServiceProvider.GetRequiredService<PrintDbContext>().Jobs.SingleAsync(x => x.EventId == id);
        Assert.Equal(PrintJobStatus.Unknown, record.Status);
        var future = Event(); future.OccurredAt = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/print-events", future, Json)).StatusCode);
    }
}
