using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Wpf.Commands;
using Microsoft.Win32;

namespace AI.Document.Converter.Wpf.ViewModels;

// UC-001/UC-002 import steps only (FR-001-005, NFR-013) - conversion itself is
// Phase 3+ and deliberately not wired up here yet (no half-finished "Convert"
// button, per docs/11-CODING-STANDARDS.md).
public sealed class DashboardViewModel : ViewModelBase
{
    private const string SupportedFilesFilter =
        "Supported Documents (*.pdf;*.docx;*.xlsx;*.pptx;*.txt)|*.pdf;*.docx;*.xlsx;*.pptx;*.txt|All files (*.*)|*.*";

    private readonly IImportService _importService;
    private readonly SettingsService _settingsService;

    private string? _statusMessage;

    public DashboardViewModel(IImportService importService, SettingsService settingsService)
    {
        _importService = importService;
        _settingsService = settingsService;

        ImportFilesCommand = new AsyncRelayCommand(ImportFilesAsync);
        ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync);
        DropFilesCommand = new AsyncRelayCommand<string[]>(paths =>
            ImportPathsAsync(ExpandDroppedPaths(paths ?? [])));
    }

    public ObservableCollection<FileImportItem> ImportedFiles { get; } = [];

    public ICommand ImportFilesCommand { get; }

    public ICommand ImportFolderCommand { get; }

    public ICommand DropFilesCommand { get; }

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
            ImportedFiles.Add(item);
        }

        StatusMessage = BuildStatusMessage(result);
    }

    private static string? BuildStatusMessage(ImportResult result)
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
