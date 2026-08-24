namespace AI.Document.Converter.Domain.ValueObjects;

public sealed class ChunkOptions
{
    public required int ChunkSizeTokens { get; init; }

    public required int OverlapTokens { get; init; }
}
