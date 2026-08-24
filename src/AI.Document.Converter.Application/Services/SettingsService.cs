using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Services;

// FR-032/033: reads/writes settings, validating every field before persisting
// (UC-005) - the ViewModel never talks to ISettingsStore directly.
public sealed class SettingsService
{
    private readonly ISettingsStore _settingsStore;
    private readonly IPythonPathValidator _pythonPathValidator;
    private readonly IPathValidator _pathValidator;

    public SettingsService(
        ISettingsStore settingsStore,
        IPythonPathValidator pythonPathValidator,
        IPathValidator pathValidator)
    {
        _settingsStore = settingsStore;
        _pythonPathValidator = pythonPathValidator;
        _pathValidator = pathValidator;
    }

    public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken) =>
        _settingsStore.LoadAsync(cancellationToken);

    public async Task<SettingsUpdateResult> UpdateSettingsAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.OutputDirectory) &&
            !_pathValidator.IsValidDirectoryPath(settings.OutputDirectory))
        {
            return Fail("The output directory path is not valid.");
        }

        if (!string.IsNullOrWhiteSpace(settings.LogDirectory) &&
            !_pathValidator.IsValidDirectoryPath(settings.LogDirectory))
        {
            return Fail("The log directory path is not valid.");
        }

        if (!string.IsNullOrWhiteSpace(settings.PythonExecutablePath) &&
            !await _pythonPathValidator.IsValidAsync(settings.PythonExecutablePath, cancellationToken))
        {
            return Fail("The configured Python executable path does not point to a valid Python interpreter.");
        }

        if (settings.ChunkSizeTokens <= 0 ||
            settings.ChunkOverlapTokens < 0 ||
            settings.ChunkOverlapTokens >= settings.ChunkSizeTokens)
        {
            return Fail("Chunk size must be positive and overlap must be smaller than the chunk size.");
        }

        if (settings.MaxParallelism <= 0)
        {
            return Fail("Maximum parallelism must be at least 1.");
        }

        if (settings.MaxBatchFiles <= 0 || settings.MaxBatchSizeBytes <= 0)
        {
            return Fail("Maximum batch size values must be positive.");
        }

        await _settingsStore.SaveAsync(settings, cancellationToken);
        return new SettingsUpdateResult { Success = true };
    }

    private static SettingsUpdateResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
