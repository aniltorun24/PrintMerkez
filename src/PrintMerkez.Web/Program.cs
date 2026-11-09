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
builder.Services.AddRazorPages();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
builder.Services.AddDbContext<PrintDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("PrintDb")
    ?? $"Data Source={Path.Combine(dataDirectory, "printmerkez.db")}"));
var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await DevelopmentSeed.InitializeAsync(scope.ServiceProvider.GetRequiredService<PrintDbContext>(),
        builder.Configuration.GetValue("Development:SeedDemoData", true));
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
app.MapGet("/health", () => Results.Ok(new { Status = "ok", Mode = "LocalDevelopment" }));
app.MapPost("/api/v1/print-events", EventIngestion.AcceptAsync).AddEndpointFilter(async (context, next) =>
{
    var expected = builder.Configuration["Agent:ApiKey"];
    if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32)
        return Results.Problem("Servis anahtarı yapılandırılmamış. scripts/start-web.ps1 ile başlatın.", statusCode: 503);
    var supplied = context.HttpContext.Request.Headers["X-Agent-Key"].ToString();
    var valid = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    return valid ? await next(context) : Results.Unauthorized();
});
app.Run();
public partial class Program;
