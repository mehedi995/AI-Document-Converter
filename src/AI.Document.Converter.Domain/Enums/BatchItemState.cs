namespace AI.Document.Converter.Domain.Enums;

// One file's progress within a running batch (FR-023). Cancelled is distinct
// from a plain failure so the UI can show a file was stopped mid-operation
// (FR-037) rather than looking like it errored out on its own.
public enum BatchItemState
{
    Started,
    Completed,
    Cancelled
}
