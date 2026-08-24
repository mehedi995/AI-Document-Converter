using Serilog;

namespace AI.Document.Converter.Infrastructure.Logging;

// FR-034/035: file-based logging, no sensitive document content ever passed to a
// log call (that rule is enforced by code review, not by this configuration).
public static class SerilogConfigurator
{
    public static void Configure(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();
    }
}
