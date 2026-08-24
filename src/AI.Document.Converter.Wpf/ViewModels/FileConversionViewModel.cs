using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Wpf.ViewModels;

// Wraps the immutable FileImportItem (Phase 2) with the mutable state a
// conversion run needs to show live - exactly the evolution flagged as
// expected back when FileImportItem.Status was introduced as a single-value
// placeholder enum. FR-045 (Phase 8): Status tracks each file's pipeline
// stage independently - Ready/Queued/Converting/Converted/Chunking/Chunked/
// Cancelled/Failed - rather than collapsing Convert and Generate Chunks into
// one indistinguishable "done" state.
public sealed class FileConversionViewModel : ViewModelBase
{
    private string _status = "Ready";
    private string? _conversionSummary;
    private string? _chunkSummary;
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

    // FR-030/AC-020: which single-file operation to re-run when the user
    // clicks Retry on this row - whichever one this row's current Status
    // actually resulted from, not necessarily the most recently started
    // batch (e.g. Convert All can still be running for other rows while this
    // row is retried individually).
    public BatchOperationKind? LastAttemptedOperation { get; set; }

    // FR-028/046: Export needs the actual ConversionResult (token estimates,
    // DocumentMetadata, chunk output path), not just the display strings
    // above - kept alongside them rather than re-derived from UI text.
    public ConversionResult? LastConversionResult { get; set; }

    public ConversionResult? LastChunkResult { get; set; }

    // Convert and Chunk are reported as two independent sub-results (FR-045)
    // rather than one accumulated string, so retrying just the chunk stage
    // replaces only the chunk half instead of stacking text onto a previous
    // attempt's message.
    public string? ConversionSummary
    {
        get => _conversionSummary;
        set
        {
            if (SetProperty(ref _conversionSummary, value))
            {
                OnPropertyChanged(nameof(ResultSummary));
            }
        }
    }

    public string? ChunkSummary
    {
        get => _chunkSummary;
        set
        {
            if (SetProperty(ref _chunkSummary, value))
            {
                OnPropertyChanged(nameof(ResultSummary));
            }
        }
    }

    public string? ResultSummary
    {
        get
        {
            var parts = new[] { ConversionSummary, ChunkSummary }.Where(s => !string.IsNullOrEmpty(s));
            var joined = string.Join(" | ", parts);
            return joined.Length == 0 ? null : joined;
        }
    }

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }
}
