namespace AI.Document.Converter.Application.Models;

// FR-005: a file rejected before it ever reaches the conversion list, with a
// user-facing reason.
public sealed class RejectedFile
{
    public required string FilePath { get; init; }

    public required string Reason { get; init; }
}
