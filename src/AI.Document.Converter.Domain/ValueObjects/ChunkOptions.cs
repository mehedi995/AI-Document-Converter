namespace AI.Document.Converter.Domain.ValueObjects;

public sealed class ChunkOptions
{
    public required int ChunkSizeTokens { get; init; }

    public required int OverlapTokens { get; init; }

    // SaaS SR-INT-6 (audit C-09). These were previously unvalidated anywhere,
    // and the two invalid combinations fail in ways that are not obvious from
    // the output:
    //
    //   size <= 0      - every unit is "oversized", so each one gets its own
    //                    dedicated chunk and the chunk count equals the block
    //                    count, silently ignoring the requested size.
    //   overlap >= size - the units carried over as overlap already fill or
    //                    exceed the next chunk, so every subsequent unit
    //                    immediately overflows. Output degenerates into
    //                    near-duplicate chunks that grow without doing useful
    //                    work.
    //
    // Neither throws on its own, which is exactly why this has to be checked
    // explicitly at the API boundary rather than left to fail confusingly later.
    public bool TryValidate(out string? error)
    {
        if (ChunkSizeTokens <= 0)
        {
            error = "Chunk size must be greater than zero.";
            return false;
        }

        if (OverlapTokens < 0)
        {
            error = "Chunk overlap cannot be negative.";
            return false;
        }

        if (OverlapTokens >= ChunkSizeTokens)
        {
            error = $"Chunk overlap ({OverlapTokens}) must be smaller than the chunk size ({ChunkSizeTokens}).";
            return false;
        }

        error = null;
        return true;
    }
}
