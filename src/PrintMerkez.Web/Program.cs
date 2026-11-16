using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

var builder = WebApplication.CreateBuilder(args);
// This milestone has no AD login yet: prevent accidental anonymous deployment.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("Bu sürüm yalnızca yerel geliştirme içindir. AD ve rol kontrolü tamamlanmadan sunucuya açılmaz.");
var culture = CultureInfo.GetCultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages();
builder.Services.AddScoped<IUsageMailSender, UsageMailSender>();
builder.Services.AddScoped<NotificationProcessor>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddSingleton<WindowsCollectorStatus>();
builder.Services.AddSingleton<IWindowsPrintReader, WindowsPrintReader>();
builder.Services.AddHostedService<WindowsCollector>();
builder.Services.AddSingleton<IDeviceJobLogClient, OlivettiDeviceJobLogClient>();
builder.Services.AddSingleton<DeviceCollectorStatus>();
builder.Services.AddHostedService<DeviceCopyCollector>();
builder.Services.AddHostedService<BackgroundStopMonitor>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
builder.Services.AddDbContext<PrintDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("PrintDb")
    ?? $"Data Source={Path.Combine(dataDirectory, "printmerkez.db")}"));
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
    if (builder.Configuration.GetValue("Maintenance:RemoveDemoData", false))
    {
        await DatabaseBootstrap.InitializeAsync(db);
        var backup = await DemoDataCleanup.BackupAsync(db, Path.Combine(dataDirectory, "backups"));
        await DemoDataCleanup.RemoveAsync(db);
        Console.WriteLine($"Örnek veriler temizlendi. Yedek: {backup}");
        return;
    }
    var seedDemo = builder.Configuration.GetValue("Development:SeedDemoData", true);
    await DevelopmentSeed.InitializeAsync(db, seedDemo);
    if (seedDemo && builder.Configuration.GetValue("Development:SeedScenarioData", false))
        {
        await ScenarioSeed.InitializeAsync(db);
        await NotificationDemoSeed.InitializeAsync(db);
    }
}
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    if (!app.Environment.IsEnvironment("Testing") && (remote is null || !IPAddress.IsLoopback(remote)))
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapGet("/health", (WindowsCollectorStatus windows, DeviceCollectorStatus device) => Results.Ok(new
{
    Status = "ok", Mode = "LocalDevelopment",
    BackgroundManaged = !string.IsNullOrWhiteSpace(builder.Configuration["Background:StopFile"]),
    WindowsCollector = windows.Current, DeviceCollector = device.Current
}));
app.MapPost("/api/v1/print-events", EventIngestion.AcceptAsync).AddEndpointFilter(async (context, next) =>
{
    var expected = builder.Configuration["Agent:ApiKey"];
    if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32)
        return Results.Problem("Servis anahtarı yapılandırılmamış. scripts/start-web.ps1 ile başlatın.", statusCode: 503);
    var supplied = context.HttpContext.Request.Headers["X-Agent-Key"].ToString();
    var valid = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    if (!valid) return Results.Unauthorized();
    if (context.Arguments[0] is PrintMerkez.Core.PrintEventRequest { Source: PrintMerkez.Core.EventSource.Simulation }
        && !builder.Configuration.GetValue("Agent:AllowSimulation", false))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["Source"] = ["Gerçek veri modunda simülasyon kabul edilmez."] });
    return await next(context);
});
app.Run();
public partial class Program;
