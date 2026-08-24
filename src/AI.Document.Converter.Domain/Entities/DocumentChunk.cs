namespace AI.Document.Converter.Domain.Entities;

public sealed class DocumentChunk
{
    public required int SequenceNumber { get; init; }

    public required string SourceFileName { get; init; }

    public required string Content { get; init; }

    public required int TokenCount { get; init; }

    public required int OverlapTokens { get; init; }
}
