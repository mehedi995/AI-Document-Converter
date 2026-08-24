using System.Text.Json;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Infrastructure.Configuration;

// Reads always come from IOptionsMonitor<AppSettings> (bound to settings.json via
// Microsoft.Extensions.Configuration with reloadOnChange: true) - a single source
// of truth every other service also reads from. Writes go straight to the file;
// the configuration system's own file watcher propagates the change back into
// IOptionsMonitor for everyone else, so there is no separate reload step here.
//
// The file path is injected (not read from AppPaths internally) so this class is
// unit-testable against a temp file (docs/17-UNIT-TEST-PLAN.md) - production
// registration (InfrastructureServiceCollectionExtensions) supplies AppPaths.SettingsFilePath.
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsFilePath;
    private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
    private readonly ILogger<JsonSettingsStore> _logger;

    public JsonSettingsStore(
        string settingsFilePath,
        IOptionsMonitor<AppSettings> optionsMonitor,
        ILogger<JsonSettingsStore> logger)
    {
        _settingsFilePath = settingsFilePath;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_optionsMonitor.CurrentValue);

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        var tempFilePath = _settingsFilePath + ".tmp";
        await File.WriteAllTextAsync(tempFilePath, json, cancellationToken);
        File.Move(tempFilePath, _settingsFilePath, overwrite: true);

        _logger.LogInformation("Settings saved to {SettingsFilePath}", _settingsFilePath);
    }
}
