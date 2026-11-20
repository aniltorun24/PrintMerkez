namespace PrintMerkez.Web.Services;

// Small daily diagnostic logs for the pilot; no request bodies or exception messages are written.
public sealed class ServerFileLoggerProvider(string directory, string role) : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string logDirectory = directory;
    private readonly string logRole = role;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(ServerFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            lock (owner.gate)
            {
                try
                {
                    Directory.CreateDirectory(owner.logDirectory);
                    foreach (var file in Directory.EnumerateFiles(owner.logDirectory, $"printmerkez-{owner.logRole}-*.log"))
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)) File.Delete(file);
                    var path = Path.Combine(owner.logDirectory, $"printmerkez-{owner.logRole}-{DateTime.UtcNow:yyyy-MM-dd}.log");
                    // Bound repetitive failure logs to 10 MB per role/day.
                    if (File.Exists(path) && new FileInfo(path).Length >= 10 * 1024 * 1024) return;
                    File.AppendAllText(path, $"{DateTime.UtcNow:O} {level} {category} event={eventId.Id} type={exception?.GetType().Name ?? "none"} {formatter(state, null).ReplaceLineEndings(" ")}\n");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Other providers still receive the log. */ }
            }
        }
    }
}
