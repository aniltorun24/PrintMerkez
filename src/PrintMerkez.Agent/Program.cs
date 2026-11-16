using PrintMerkez.Agent;
var builder = Host.CreateApplicationBuilder(args);
if (builder.Configuration.GetValue("Windows:ReadOnce", false))
{
    if (builder.Configuration.GetValue("Agent:Simulate", false))
        throw new InvalidOperationException("Windows okuması ve simülasyon birlikte seçilemez.");
    if (!OperatingSystem.IsWindows())
        throw new PlatformNotSupportedException("Windows olay günlüğü okuması Windows gerektirir.");
    try { Environment.ExitCode = WindowsPrintProbe.Run(builder.Configuration); }
    catch (Exception ex) when (ex is System.Diagnostics.Eventing.Reader.EventLogException
        or UnauthorizedAccessException or System.Security.SecurityException)
    {
        Console.Error.WriteLine("Windows olay günlüğü okunamadı. Sunucu adı, ağ erişimi ve olay günlüğü okuma yetkisini kontrol edin.");
        Environment.ExitCode = 1;
    }
    return;
}
if (!builder.Configuration.GetValue("Agent:Simulate", false))
{
    Console.WriteLine("Mod seçin: Agent:Simulate=true veya salt okunur pilot için Windows:ReadOnce=true.");
    return;
}
var api = builder.Configuration["Agent:ApiUrl"] ?? "http://127.0.0.1:5180";
if (!Uri.TryCreate(api, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http")
    throw new InvalidOperationException("Simülatör yalnızca yerel HTTP API adresini kabul eder.");
var key = builder.Configuration["Agent:ApiKey"];
if (string.IsNullOrWhiteSpace(key) || key.Length < 32) throw new InvalidOperationException("Agent:ApiKey yapılandırılmalı.");
var client = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(10) };
client.DefaultRequestHeaders.Add("X-Agent-Key", key);
builder.Services.AddSingleton(client);
builder.Services.AddHostedService<SimulationWorker>();
await builder.Build().RunAsync();
