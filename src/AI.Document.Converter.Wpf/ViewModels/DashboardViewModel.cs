using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Wpf.Commands;
using Microsoft.Win32;

namespace AI.Document.Converter.Wpf.ViewModels;

// UC-001 (Phase 6/7): the single-file conversion/chunking pipeline lives in
// IConversionService. Phase 8 adds IBatchService on top to run that pipeline
// over many files with bounded parallelism (NFR-011), live per-file progress
// (FR-023), and cancellation (FR-037) - it does not replace ConvertAsync/
// GenerateChunksAsync, it orchestrates them.
public sealed class DashboardViewModel : ViewModelBase
{
    private const string SupportedFilesFilter =
        "Supported Documents (*.pdf;*.docx;*.xlsx;*.pptx;*.txt)|*.pdf;*.docx;*.xlsx;*.pptx;*.txt|All files (*.*)|*.*";

    private readonly IImportService _importService;
    private readonly IConversionService _conversionService;
    private readonly IBatchService _batchService;
    private readonly IExportService _exportService;
    private readonly SettingsService _settingsService;

    private string? _statusMessage;
    private bool _isBusy;
    private CancellationTokenSource? _batchCancellationTokenSource;

    public DashboardViewModel(
        IImportService importService,
        IConversionService conversionService,
        IBatchService batchService,
        IExportService exportService,
        SettingsService settingsService)
    {
        _importService = importService;
        _conversionService = conversionService;
        _batchService = batchService;
        _exportService = exportService;
        _settingsService = settingsService;

        ImportFilesCommand = new AsyncRelayCommand(ImportFilesAsync);
        ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync);
        DropFilesCommand = new AsyncRelayCommand<string[]>(paths =>
            ImportPathsAsync(ExpandDroppedPaths(paths ?? [])));
        ConvertAllCommand = new AsyncRelayCommand(ConvertAllAsync, () => !IsBusy && ImportedFiles.Count > 0);
        GenerateChunksCommand = new AsyncRelayCommand(
            GenerateChunksAllAsync, () => !IsBusy && ImportedFiles.Any(f => f.Status == "Converted"));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        RetryCommand = new AsyncRelayCommand<FileConversionViewModel>(
            RetryAsync, item => !IsBusy && item?.Status == "Failed");
        ExportCommand = new AsyncRelayCommand(
            ExportAsZipAsync, () => !IsBusy && ImportedFiles.Any(f => f.LastConversionResult?.Success == true));
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

    // FR-037: cancels whichever batch (Convert All or Generate Chunks) is
    // currently running; disabled otherwise via CanExecute.
    public ICommand CancelCommand { get; }

    // FR-030/AC-020: re-attempts just the one failed row's last-attempted
    // stage (Convert or Chunk) - enabled only while that row's Status is
    // "Failed" and no other batch is currently running.
    public ICommand RetryCommand { get; }

    // FR-028: packages every successfully converted file's output (plus
    // chunks, when generated) into a single ZIP - enabled once at least one
    // file has converted successfully.
    public ICommand ExportCommand { get; }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
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

        foreach (var item in ImportedFiles)
        {
            item.Status = "Queued";
            item.ConversionSummary = null;
            item.ChunkSummary = null;
            item.IsError = false;
            item.LastAttemptedOperation = BatchOperationKind.Convert;
        }

        await RunConvertBatchAsync(ImportedFiles.ToList(), settings);
    }

    private async Task GenerateChunksAllAsync()
    {
        var settings = await _settingsService.GetSettingsAsync(CancellationToken.None);

        // Only files that already converted successfully - chunking a file
        // that never produced valid content wouldn't mean anything.
        var items = ImportedFiles.Where(f => f.Status == "Converted").ToList();
        foreach (var item in items)
        {
            item.LastAttemptedOperation = BatchOperationKind.Chunk;
        }

        await RunChunkBatchAsync(items, settings);
    }

    // FR-030/AC-020: re-runs whichever single operation this row's current
    // "Failed" status came from, for just this one file - shares
    // RunConvertBatchAsync/RunChunkBatchAsync (and so IsBusy/cancellation/
    // progress) with the "All" commands via a one-item list rather than
    // duplicating that plumbing for a single-file path.
    private async Task RetryAsync(FileConversionViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsError = false;
        var settings = await _settingsService.GetSettingsAsync(CancellationToken.None);

        if (item.LastAttemptedOperation == BatchOperationKind.Chunk)
        {
            await RunChunkBatchAsync([item], settings);
        }
        else
        {
            await RunConvertBatchAsync([item], settings);
        }
    }

    private async Task RunConvertBatchAsync(IReadOnlyList<FileConversionViewModel> items, AppSettings settings)
    {
        var outputPathResolver = new OutputPathResolver();
        var itemsByPath = items.ToDictionary(f => f.ImportItem.FilePath);

        await RunBatchAsync(
            itemsByPath.Keys.ToList(),
            (filePath, token) => _conversionService.ConvertAsync(
                filePath, settings.OutputDirectory, outputPathResolver, token),
            settings.MaxParallelism,
            update => ApplyConvertProgress(itemsByPath, update),
            summary => $"Converted {summary.SuccessCount} file(s); {summary.FailureCount} failed." +
                       (summary.ProcessedFiles < summary.TotalFiles ? " (cancelled)" : string.Empty));
    }

    private async Task RunChunkBatchAsync(IReadOnlyList<FileConversionViewModel> items, AppSettings settings)
    {
        var chunkOptions = new ChunkOptions
        {
            ChunkSizeTokens = settings.ChunkSizeTokens,
            OverlapTokens = settings.ChunkOverlapTokens
        };
        var itemsByPath = items.ToDictionary(f => f.ImportItem.FilePath);

        await RunBatchAsync(
            itemsByPath.Keys.ToList(),
            (filePath, token) => _conversionService.GenerateChunksAsync(
                filePath, settings.OutputDirectory, chunkOptions, token),
            settings.MaxParallelism,
            update => ApplyChunkProgress(itemsByPath, update),
            summary => $"Generated chunks for {summary.SuccessCount} file(s); {summary.FailureCount} failed." +
                       (summary.ProcessedFiles < summary.TotalFiles ? " (cancelled)" : string.Empty));
    }

    // FR-023: with real parallelism, several files are "in progress" at
    // once, so each row tracks its own Started/Completed transition rather
    // than a single "current file".
    private static void ApplyConvertProgress(
        Dictionary<string, FileConversionViewModel> itemsByPath, BatchProgressUpdate update)
    {
        if (!itemsByPath.TryGetValue(update.FilePath, out var item))
        {
            return;
        }

        switch (update.State)
        {
            case BatchItemState.Started:
                item.Status = "Converting";
                break;
            case BatchItemState.Cancelled:
                item.Status = "Cancelled";
                break;
            case BatchItemState.Completed when update.Result!.Success:
                item.Status = "Converted";
                item.ConversionSummary = BuildResultSummary(update.Result);
                item.LastConversionResult = update.Result;
                break;
            case BatchItemState.Completed:
                item.Status = "Failed";
                item.IsError = true;
                item.ConversionSummary = update.Result!.ErrorMessage;
                item.LastConversionResult = update.Result;
                break;
        }
    }

    // FR-045: Chunked/Failed here is this file's chunking-stage status,
    // distinct from the "Converted" status Convert All already gave it - a
    // file can be Converted but Failed at the chunking stage, or vice versa
    // on a later run. ChunkSummary is replaced wholesale (not appended) so a
    // retry's outcome doesn't stack onto a previous attempt's message.
    private static void ApplyChunkProgress(
        Dictionary<string, FileConversionViewModel> itemsByPath, BatchProgressUpdate update)
    {
        if (!itemsByPath.TryGetValue(update.FilePath, out var item))
        {
            return;
        }

        switch (update.State)
        {
            case BatchItemState.Started:
                item.Status = "Chunking";
                break;
            case BatchItemState.Cancelled:
                item.Status = "Cancelled";
                break;
            case BatchItemState.Completed when update.Result!.Success:
                item.Status = "Chunked";
                item.ChunkSummary = $"{update.Result.ChunkCount} chunk(s)";
                item.LastChunkResult = update.Result;
                break;
            case BatchItemState.Completed:
                item.Status = "Failed";
                item.IsError = true;
                item.ChunkSummary = $"Chunking failed: {update.Result!.ErrorMessage}";
                item.LastChunkResult = update.Result;
                break;
        }
    }

    // Shared by Convert All, Generate Chunks, and Retry (FR-022-025/FR-030/
    // FR-037/NFR-011) so all three go through the same IsBusy/cancellation/
    // progress plumbing instead of duplicating it.
    private async Task RunBatchAsync(
        IReadOnlyList<string> filePaths,
        Func<string, CancellationToken, Task<ConversionResult>> operation,
        int maxParallelism,
        Action<BatchProgressUpdate> onProgress,
        Func<BatchSummary, string> buildStatusMessage)
    {
        IsBusy = true;
        _batchCancellationTokenSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<BatchProgressUpdate>(onProgress);
            var summary = await _batchService.RunAsync(
                filePaths, operation, maxParallelism, progress, _batchCancellationTokenSource.Token);

            StatusMessage = buildStatusMessage(summary);
        }
        finally
        {
            _batchCancellationTokenSource?.Dispose();
            _batchCancellationTokenSource = null;
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        _batchCancellationTokenSource?.Cancel();
    }

    // FR-028/046: packages every file that has ever converted successfully
    // (LastConversionResult, not just the current run's Status) into one
    // ZIP, with LastChunkResult included per file only when that file was
    // also chunked.
    private async Task ExportAsZipAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ZIP Package (*.zip)|*.zip",
            FileName = $"export-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Title = "Export as ZIP"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var items = ImportedFiles
            .Where(f => f.LastConversionResult is { Success: true })
            .Select(f => new ExportItem
            {
                SourceFilePath = f.ImportItem.FilePath,
                ConversionResult = f.LastConversionResult!,
                ChunkResult = f.LastChunkResult
            })
            .ToList();

        IsBusy = true;
        try
        {
            var result = await _exportService.ExportBatchAsZipAsync(items, dialog.FileName, CancellationToken.None);

            StatusMessage = result.Success
                ? $"Exported {result.ExportedFileCount} file(s) to {result.ZipPath}."
                : result.ErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
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
