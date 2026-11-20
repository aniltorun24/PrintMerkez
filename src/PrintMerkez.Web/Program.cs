using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});
var sharedSettings = builder.Configuration["Deployment:SettingsFile"];
if (!string.IsNullOrWhiteSpace(sharedSettings))
    builder.Configuration.AddJsonFile(sharedSettings, optional: false, reloadOnChange: false)
        .AddEnvironmentVariables().AddCommandLine(args);
ServerDeployment.Validate(builder.Configuration, builder.Environment);
var server = ServerDeployment.Enabled(builder.Configuration);
var collectorOnly = server && ServerDeployment.Collector(builder.Configuration);
if (collectorOnly) builder.Services.AddWindowsService(options => options.ServiceName = "PrintMerkezCollector");
var culture = CultureInfo.GetCultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages();
builder.Services.AddSingleton<LocalAdminCredentials>();
builder.Services.AddScoped<UserDailyUsage>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Login";
    options.Cookie.Name = "PrintMerkez.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/live") || context.Request.Query["handler"] == "Live")
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy =>
    policy.RequireAuthenticatedUser().RequireRole("Admin")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.ToString().TrimEnd('/').Equals("/Login", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(context.Request.Method)
            ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other"));
    options.OnRejected = async (context, ct) =>
        await context.HttpContext.Response.WriteAsync("Çok fazla giriş denemesi. Bir dakika sonra tekrar deneyin.", ct);
});
builder.Services.AddSingleton<LiveUpdates>();
builder.Services.AddOutputCache(options => options.AddPolicy("LiveSnapshots", policy => policy
    .With(context => context.HttpContext.Request.Query["handler"] == "Live"
        && (context.HttpContext.Request.Path == "/" || context.HttpContext.Request.Path == "/Jobs"))
    .Expire(TimeSpan.FromSeconds(2)).SetVaryByQuery("*")));
builder.Services.AddScoped<IUsageMailSender, UsageMailSender>();
builder.Services.AddScoped<NotificationProcessor>();
builder.Services.AddSingleton<WindowsCollectorStatus>();
builder.Services.AddSingleton<IWindowsPrintReader, WindowsPrintReader>();
builder.Services.AddSingleton<IDeviceJobLogClient, OlivettiDeviceJobLogClient>();
builder.Services.AddSingleton<DeviceCollectorStatus>();
ServerDeployment.AddRuntimeServices(builder.Services, builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var dataDirectory = server ? builder.Configuration["Deployment:DataDirectory"]!
    : Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
if (server)
{
    var logDirectory = builder.Configuration["Deployment:LogDirectory"] ?? Path.Combine(dataDirectory, "Logs");
    builder.Logging.AddProvider(new ServerFileLoggerProvider(logDirectory, collectorOnly ? "Collector" : "Web"));
    if (!collectorOnly)
    {
        var keys = Directory.CreateDirectory(Path.Combine(dataDirectory, "Keys"));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Bu sunucu paketi Windows içindir.");
        builder.Services.AddDataProtection().SetApplicationName("PrintMerkez.Server")
            .PersistKeysToFileSystem(keys).ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}
builder.Services.AddDbContext<PrintDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("PrintDb")
    ?? $"Data Source={Path.Combine(dataDirectory, "printmerkez.db")}"));
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
    if (!string.IsNullOrWhiteSpace(builder.Configuration["Maintenance:ExportDirectory"]))
    {
        await ServerSnapshot.ExportAsync(db, builder.Configuration, builder.Environment,
            builder.Configuration["Maintenance:ExportDirectory"]!);
        Console.WriteLine("Tutarlı veritabanı ve devam noktası yayın paketine alındı. Toplayıcılar başlatılmadı.");
        return;
    }
    if (server)
    {
        if (builder.Configuration.GetValue("Maintenance:InitializeDatabase", false))
        {
            await DatabaseBootstrap.InitializeAsync(db);
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            Console.WriteLine("Sunucu veritabanı hazırlandı. Veri toplama başlatılmadı.");
            return;
        }
        var databasePath = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (!File.Exists(databasePath) || (await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Sunucu veritabanı hazır değil. Önce kontrollü veritabanı hazırlığını çalıştırın.");
    }
    else
    {
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
    var directoryFile=builder.Configuration["Maintenance:ImportDirectoryFile"];
    if(!string.IsNullOrWhiteSpace(directoryFile))
    {
        var export=System.Text.Json.JsonSerializer.Deserialize<DirectoryExport>(await File.ReadAllTextAsync(directoryFile))
            ?? throw new InvalidOperationException("AD dosyası okunamadı.");
        var preview=builder.Configuration.GetValue("Maintenance:DirectoryImportPreview",true);
        if(!preview)await DemoDataCleanup.BackupAsync(db,Path.Combine(dataDirectory,"backups"));
        var report=await new DirectoryImport(db).RunAsync(export,preview);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
        return;
    }
}
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    if (!ServerDeployment.Allows(remote, context.Request.IsHttps, builder.Configuration, app.Environment.IsEnvironment("Testing")))
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true || context.Request.Path.StartsWithSegments("/Login"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseOutputCache();
if (!collectorOnly)
{
    app.MapRazorPages().RequireAuthorization("Admin").CacheOutput("LiveSnapshots");
    app.MapGet("/live/events", LiveUpdates.Stream).RequireAuthorization("Admin");
}
app.MapGet("/health", (HttpContext context, WindowsCollectorStatus windows, DeviceCollectorStatus device) => Results.Ok(new
{
    Status = "ok", Mode = server ? (collectorOnly ? "ServerCollector" : "ServerWeb") : "LocalDevelopment",
    BackgroundManaged = !string.IsNullOrWhiteSpace(builder.Configuration["Background:StopFile"]),
    WindowsCollector = context.User.IsInRole("Admin") ? windows.Current : null,
    DeviceCollector = context.User.IsInRole("Admin") ? device.Current : null
}));
if (!collectorOnly) app.MapPost("/api/v1/print-events", EventIngestion.AcceptAsync).AddEndpointFilter(async (context, next) =>
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
