using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Wpf.Commands;
using Microsoft.Win32;

namespace AI.Document.Converter.Wpf.ViewModels;

// UC-001 (Phase 6): a real, working single-file conversion pipeline -
// sequential, one file at a time. Bounded parallelism, live progress, and
// cancellation are Phase 8's BatchService, built on top of this same
// IConversionService rather than replacing it.
public sealed class DashboardViewModel : ViewModelBase
{
    private const string SupportedFilesFilter =
        "Supported Documents (*.pdf;*.docx;*.xlsx;*.pptx;*.txt)|*.pdf;*.docx;*.xlsx;*.pptx;*.txt|All files (*.*)|*.*";

    private readonly IImportService _importService;
    private readonly IConversionService _conversionService;
    private readonly SettingsService _settingsService;

    private string? _statusMessage;

    public DashboardViewModel(
        IImportService importService,
        IConversionService conversionService,
        SettingsService settingsService)
    {
        _importService = importService;
        _conversionService = conversionService;
        _settingsService = settingsService;

        ImportFilesCommand = new AsyncRelayCommand(ImportFilesAsync);
        ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync);
        DropFilesCommand = new AsyncRelayCommand<string[]>(paths =>
            ImportPathsAsync(ExpandDroppedPaths(paths ?? [])));
        ConvertAllCommand = new AsyncRelayCommand(ConvertAllAsync, () => ImportedFiles.Count > 0);
        GenerateChunksCommand = new AsyncRelayCommand(
            GenerateChunksAllAsync, () => ImportedFiles.Any(f => f.Status == "Converted"));
    }

    public ObservableCollection<FileConversionViewModel> ImportedFiles { get; } = [];

    public ICommand ImportFilesCommand { get; }

    public ICommand ImportFolderCommand { get; }

    public ICommand DropFilesCommand { get; }

    public ICommand ConvertAllCommand { get; }

    // FR-018-021 (Phase 7): a distinct action from Convert All, matching
    // CLAUDE.md's Chunk Settings screen having its own "Generate Chunks"
    // trigger. Chunk size/overlap are NOT duplicated in a separate screen -
    // they already live on the main Settings screen (Phase 1's
    // AppSettings.ChunkSizeTokens/ChunkOverlapTokens); this command just uses
    // whatever is currently configured there.
    public ICommand GenerateChunksCommand { get; }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private async Task ImportFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = SupportedFilesFilter,
            Title = "Select files to import"
        };

        if (dialog.ShowDialog() == true)
        {
            await ImportPathsAsync(dialog.FileNames);
        }
    }

    private async Task ImportFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder to import" };

        if (dialog.ShowDialog() == true)
        {
            // FR-004: top-level only for MVP - subfolders are not scanned.
            var filePaths = Directory.GetFiles(dialog.FolderName, "*", SearchOption.TopDirectoryOnly);
            await ImportPathsAsync(filePaths);
        }
    }

    // FR-003: Explorer allows dragging a mix of files and folders onto the
    // window; folders are expanded to their top-level files just like FR-004.
    private static IReadOnlyList<string> ExpandDroppedPaths(IReadOnlyList<string> droppedPaths)
    {
        var expanded = new List<string>();

        foreach (var path in droppedPaths)
        {
            if (Directory.Exists(path))
            {
                expanded.AddRange(Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly));
            }
            else if (File.Exists(path))
            {
                expanded.Add(path);
            }
        }

        return expanded;
    }

    private async Task ImportPathsAsync(IReadOnlyList<string> filePaths)
    {
        var settings = await _settingsService.GetSettingsAsync(CancellationToken.None);
        var currentSizeBytes = 0L;
        foreach (var item in ImportedFiles)
        {
            currentSizeBytes += item.FileSizeBytes;
        }

        var result = _importService.Import(filePaths, ImportedFiles.Count, currentSizeBytes, settings);

        foreach (var item in result.AcceptedFiles)
        {
            ImportedFiles.Add(new FileConversionViewModel(item));
        }

        StatusMessage = BuildImportStatusMessage(result);
    }

    private async Task ConvertAllAsync()
    {
        var settings = await _settingsService.GetSettingsAsync(CancellationToken.None);
        var outputPathResolver = new OutputPathResolver();
        var successCount = 0;
        var failureCount = 0;

        // Sequential on purpose (bounded parallelism is Phase 8, NFR-011) -
        // one file's failure never stops the rest (BR-006).
        foreach (var item in ImportedFiles)
        {
            item.Status = "Converting";
            item.ResultSummary = null;
            item.IsError = false;

            var result = await _conversionService.ConvertAsync(
                item.ImportItem.FilePath,
                settings.OutputDirectory,
                outputPathResolver,
                CancellationToken.None);

            if (result.Success)
            {
                successCount++;
                item.Status = "Converted";
                item.ResultSummary = BuildResultSummary(result);
            }
            else
            {
                failureCount++;
                item.Status = "Failed";
                item.IsError = true;
                item.ResultSummary = result.ErrorMessage;
            }
        }

        StatusMessage = $"Converted {successCount} file(s); {failureCount} failed.";
    }

    private async Task GenerateChunksAllAsync()
    {
        var settings = await _settingsService.GetSettingsAsync(CancellationToken.None);
        var chunkOptions = new ChunkOptions
        {
            ChunkSizeTokens = settings.ChunkSizeTokens,
            OverlapTokens = settings.ChunkOverlapTokens
        };
        var chunkedCount = 0;
        var failureCount = 0;

        // Only files that already converted successfully - chunking a file
        // that never produced valid content wouldn't mean anything.
        foreach (var item in ImportedFiles.Where(f => f.Status == "Converted"))
        {
            var result = await _conversionService.GenerateChunksAsync(
                item.ImportItem.FilePath, settings.OutputDirectory, chunkOptions, CancellationToken.None);

            if (result.Success)
            {
                chunkedCount++;
                item.ResultSummary += $" | {result.ChunkCount} chunk(s)";
            }
            else
            {
                failureCount++;
                item.IsError = true;
                item.ResultSummary += $" | Chunking failed: {result.ErrorMessage}";
            }
        }

        StatusMessage = $"Generated chunks for {chunkedCount} file(s); {failureCount} failed.";
    }

    private static string BuildResultSummary(ConversionResult result)
    {
        var tokens = result.Tokens;
        if (tokens is null)
        {
            return $"Saved to {result.OutputPath}";
        }

        return
            $"Claude-style (est.): {tokens.OriginalClaudeStyle:N0} → {tokens.ConvertedClaudeStyle:N0} tokens | " +
            $"GPT-4o-style (est.): {tokens.OriginalGpt4oStyle:N0} → {tokens.ConvertedGpt4oStyle:N0} tokens " +
            $"({tokens.ReductionPercentGpt4oStyle:0.#}% reduction) → {Path.GetFileName(result.OutputPath)}";
    }

    private static string? BuildImportStatusMessage(ImportResult result)
    {
        var parts = new List<string>();

        if (result.RejectedFiles.Count > 0)
        {
            parts.Add($"{result.RejectedFiles.Count} file(s) skipped (unsupported type).");
        }

        if (result.FilesExcludedByBatchLimit > 0)
        {
            parts.Add($"{result.FilesExcludedByBatchLimit} file(s) skipped (batch limit reached).");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
