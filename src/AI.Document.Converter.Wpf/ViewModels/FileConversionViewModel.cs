using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Wpf.ViewModels;

// Wraps the immutable FileImportItem (Phase 2) with the mutable state a
// conversion run needs to show live - exactly the evolution flagged as
// expected back when FileImportItem.Status was introduced as a single-value
// placeholder enum. Phase 8 (Batch Processing) will extend Status further
// with live per-file progress; this phase only needs Ready/Converting/
// Converted/Failed.
public sealed class FileConversionViewModel : ViewModelBase
{
    private string _status = "Ready";
    private string? _resultSummary;
    private bool _isError;

    public FileConversionViewModel(FileImportItem importItem)
    {
        ImportItem = importItem;
    }

    public FileImportItem ImportItem { get; }

    public string FileName => ImportItem.FileName;

    public SupportedFileType FileType => ImportItem.FileType;

    public long FileSizeBytes => ImportItem.FileSizeBytes;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string? ResultSummary
    {
        get => _resultSummary;
        set => SetProperty(ref _resultSummary, value);
    }

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }
}
