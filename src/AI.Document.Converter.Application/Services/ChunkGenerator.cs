using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Services;

// FR-018-021. The document is first flattened into "atomic units" - a
// section's heading is glued to its first block (FR-020: never separated),
// every other block stands alone - each rendered via the same
// MarkdownBlockRenderer the full document uses (so a chunk's text is never
// out of sync with how MarkdownGenerator would have rendered it). Real
// per-unit token counts come from ONE batched ITokenCounter call for the
// whole document, not one call per unit - see ITokenCounter's own comment for
// why that matters. Chunk assembly itself is then a simple, exact greedy
// bin-pack with no further Python calls needed.
public sealed class ChunkGenerator : IChunkGenerator
{
    private readonly ITokenCounter _tokenCounter;

    public ChunkGenerator(ITokenCounter tokenCounter)
    {
        _tokenCounter = tokenCounter;
    }

    public async Task<IReadOnlyList<DocumentChunk>> GenerateChunksAsync(
        DocumentModel document,
        ChunkOptions options,
        string sourceFileName,
        CancellationToken cancellationToken)
    {
        var units = Flatten(document);
        if (units.Count == 0)
        {
            return [];
        }

        var tokenCounts = await _tokenCounter.CountBatchAsync(
            units.Select(u => u.Text).ToList(), cancellationToken);

        var measuredUnits = units
            .Zip(tokenCounts, (unit, count) => new MeasuredUnit(unit.Text, count))
            .ToList();

        return BuildChunks(measuredUnits, options, sourceFileName);
    }

    private static List<AtomicUnit> Flatten(DocumentModel document)
    {
        var units = new List<AtomicUnit>();

        foreach (var section in document.Sections)
        {
            // FR-020: the heading (and any page/slide reference) is glued to
            // the section's first block as a single atomic unit, so the
            // chunker can never place them in different chunks.
            var prefix = MarkdownBlockRenderer.RenderHeading(section) + MarkdownBlockRenderer.RenderReference(section.Location);

            if (section.Blocks.Count == 0)
            {
                if (prefix.Length > 0)
                {
                    units.Add(new AtomicUnit(prefix));
                }

                continue;
            }

            for (var i = 0; i < section.Blocks.Count; i++)
            {
                var blockText = MarkdownBlockRenderer.RenderBlock(section.Blocks[i]);
                units.Add(new AtomicUnit(i == 0 ? prefix + blockText : blockText));
            }
        }

        return units;
    }

    private static List<DocumentChunk> BuildChunks(
        IReadOnlyList<MeasuredUnit> units,
        ChunkOptions options,
        string sourceFileName)
    {
        var chunks = new List<DocumentChunk>();
        var current = new List<MeasuredUnit>();
        var currentTokens = 0;
        var currentOverlapTokens = 0;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            chunks.Add(new DocumentChunk
            {
                SequenceNumber = chunks.Count + 1,
                SourceFileName = sourceFileName,
                Content = string.Concat(current.Select(u => u.Text)).TrimEnd() + "\n",
                TokenCount = currentTokens,
                OverlapTokens = currentOverlapTokens
            });
        }

        foreach (var unit in units)
        {
            // FR-019/AC-015: a single unit bigger than the configured chunk
            // size (e.g., a large table) gets its own dedicated chunk rather
            // than ever being split.
            var isOversized = unit.TokenCount > options.ChunkSizeTokens;
            var wouldOverflow = !isOversized
                && current.Count > 0
                && currentTokens + unit.TokenCount > options.ChunkSizeTokens;

            if (isOversized || wouldOverflow)
            {
                var overlapUnits = isOversized
                    ? []
                    : TakeTrailingUnitsForOverlap(current, options.OverlapTokens);

                Flush();

                current = [.. overlapUnits];
                currentTokens = overlapUnits.Sum(u => u.TokenCount);
                currentOverlapTokens = currentTokens;
            }

            if (isOversized)
            {
                current.Add(unit);
                currentTokens += unit.TokenCount;
                Flush();

                current = [];
                currentTokens = 0;
                currentOverlapTokens = 0;
                continue;
            }

            current.Add(unit);
            currentTokens += unit.TokenCount;
        }

        Flush();

        return chunks;
    }

    // Whole trailing units (never a partial unit), so overlap never splits a
    // table/heading+content pairing either - it just repeats them verbatim at
    // the start of the next chunk (AC-014: no content lost across chunks).
    private static List<MeasuredUnit> TakeTrailingUnitsForOverlap(
        IReadOnlyList<MeasuredUnit> units,
        int overlapTokens)
    {
        if (overlapTokens <= 0)
        {
            return [];
        }

        var result = new List<MeasuredUnit>();
        var accumulated = 0;

        for (var i = units.Count - 1; i >= 0 && accumulated < overlapTokens; i--)
        {
            result.Insert(0, units[i]);
            accumulated += units[i].TokenCount;
        }

        return result;
    }

    private sealed record AtomicUnit(string Text);

    private sealed record MeasuredUnit(string Text, int TokenCount);
}
