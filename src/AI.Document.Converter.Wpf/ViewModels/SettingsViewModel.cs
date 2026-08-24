using System.Windows.Input;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Wpf.Commands;

namespace AI.Document.Converter.Wpf.ViewModels;

// FR-032/033, UC-005. Fields not yet exposed here (PythonEngineTimeoutSeconds,
// TokenizerDisplayProviders - added when their owning phase builds a screen for
// them) are read from the current settings and carried through unchanged on save,
// so saving this screen never silently resets them (AC-022).
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;

    private string _outputDirectory = string.Empty;
    private string _logDirectory = string.Empty;
    private string _pythonExecutablePath = string.Empty;
    private int _chunkSizeTokens;
    private int _chunkOverlapTokens;
    private int _maxParallelism;
    private int _maxBatchFiles;
    private long _maxBatchSizeBytes;
    private string? _statusMessage;
    private bool _isError;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    public string LogDirectory
    {
        get => _logDirectory;
        set => SetProperty(ref _logDirectory, value);
    }

    public string PythonExecutablePath
    {
        get => _pythonExecutablePath;
        set => SetProperty(ref _pythonExecutablePath, value);
    }

    public int ChunkSizeTokens
    {
        get => _chunkSizeTokens;
        set => SetProperty(ref _chunkSizeTokens, value);
    }

    public int ChunkOverlapTokens
    {
        get => _chunkOverlapTokens;
        set => SetProperty(ref _chunkOverlapTokens, value);
    }

    public int MaxParallelism
    {
        get => _maxParallelism;
        set => SetProperty(ref _maxParallelism, value);
    }

    public int MaxBatchFiles
    {
        get => _maxBatchFiles;
        set => SetProperty(ref _maxBatchFiles, value);
    }

    public long MaxBatchSizeBytes
    {
        get => _maxBatchSizeBytes;
        set => SetProperty(ref _maxBatchSizeBytes, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public ICommand SaveCommand { get; }

    public async Task LoadAsync()
    {
        var settings = await _settingsService.GetSettingsAsync(CancellationToken.None);
        ApplyToFields(settings);
        StatusMessage = null;
    }

    private async Task SaveAsync()
    {
        var current = await _settingsService.GetSettingsAsync(CancellationToken.None);

        var candidate = new AppSettings
        {
            OutputDirectory = OutputDirectory,
            LogDirectory = LogDirectory,
            PythonExecutablePath = PythonExecutablePath,
            PythonEngineTimeoutSeconds = current.PythonEngineTimeoutSeconds,
            ChunkSizeTokens = ChunkSizeTokens,
            ChunkOverlapTokens = ChunkOverlapTokens,
            MaxParallelism = MaxParallelism,
            MaxBatchFiles = MaxBatchFiles,
            MaxBatchSizeBytes = MaxBatchSizeBytes,
            TokenizerDisplayProviders = current.TokenizerDisplayProviders
        };

        var result = await _settingsService.UpdateSettingsAsync(candidate, CancellationToken.None);

        IsError = !result.Success;
        StatusMessage = result.Success ? "Settings saved." : result.ErrorMessage;
    }

    private void ApplyToFields(AppSettings settings)
    {
        OutputDirectory = settings.OutputDirectory;
        LogDirectory = settings.LogDirectory;
        PythonExecutablePath = settings.PythonExecutablePath;
        ChunkSizeTokens = settings.ChunkSizeTokens;
        ChunkOverlapTokens = settings.ChunkOverlapTokens;
        MaxParallelism = settings.MaxParallelism;
        MaxBatchFiles = settings.MaxBatchFiles;
        MaxBatchSizeBytes = settings.MaxBatchSizeBytes;
    }
}
