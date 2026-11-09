using PrintMerkez.Agent;
var builder = Host.CreateApplicationBuilder(args);
if (!builder.Configuration.GetValue("Agent:Simulate", false))
{
    Console.WriteLine("Windows olay okuyucusu henüz eklenmedi. Yalnızca açıkça seçilen simülasyon modu kullanılabilir.");
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