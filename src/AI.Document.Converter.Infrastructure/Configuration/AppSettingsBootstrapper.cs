using System.Text.Json;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.FileSystem;

namespace AI.Document.Converter.Infrastructure.Configuration;

// Runs synchronously, once, before the DI container (and therefore Serilog and
// IConfiguration) is built - so the very first configuration read already sees a
// fully-populated settings file rather than the AppSettings POCO's bare defaults.
public static class AppSettingsBootstrapper
{
    public static void EnsureSettingsFileExists()
    {
        if (File.Exists(AppPaths.SettingsFilePath))
        {
            return;
        }

        var defaults = new AppSettings
        {
            OutputDirectory = AppPaths.DefaultOutputDirectory,
            LogDirectory = AppPaths.DefaultLogDirectory,
            PythonExecutablePath = AppPaths.BundledPythonEnginePath,
            MaxParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        };

        WriteSettingsFile(defaults);
    }

    // Read directly, before the DI container exists, so Serilog can be configured
    // with the user's chosen log directory from the very first log line (FR-034).
    public static string ReadLogDirectoryForBootstrap()
    {
        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return string.IsNullOrWhiteSpace(settings?.LogDirectory)
                ? AppPaths.DefaultLogDirectory
                : settings.LogDirectory;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppPaths.DefaultLogDirectory;
        }
    }

    private static void WriteSettingsFile(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(AppPaths.SettingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsFilePath, json);
    }
}
