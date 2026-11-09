using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintMerkez.Core;
namespace PrintMerkez.Agent;
// Development transport exercise; not a Windows spooler collector and never submits a print job.
public sealed class SimulationWorker(HttpClient client, IConfiguration config, ILogger<SimulationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        PrintEventRequest? pending = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            pending ??= new PrintEventRequest
            {
                EventId = Guid.NewGuid().ToString("N"), Server = "LOCAL-SIMULATOR",
                PrinterId = config.GetValue("Agent:PrinterId", 1), Account = "ORNEK\\simulator",
                OccurredAt = DateTimeOffset.UtcNow, Impressions = Random.Shared.Next(1,21),
                Copies = 1, Color = PrintColor.Unknown, Status = PrintJobStatus.QueueCompleted, Source = EventSource.Simulation
            };
            try
            {
                using var response = await client.PostAsJsonAsync("/api/v1/print-events", pending, options, stoppingToken);
                if (response.IsSuccessStatusCode) { logger.LogInformation("Simülasyon olayı kabul edildi: {EventId}", pending.EventId); pending = null; }
                else logger.LogWarning("API yanıtı: {Status}. Aynı olay yeniden denenecek.", response.StatusCode);
            }
            catch (HttpRequestException) { logger.LogWarning("Yerel API erişilemiyor. Aynı olay yeniden denenecek."); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning("API zaman aşımı. Aynı olay yeniden denenecek."); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}