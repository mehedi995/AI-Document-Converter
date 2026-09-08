using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
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

    public async Task<ChunkGenerationResult> GenerateChunksAsync(
        DocumentModel document,
        ChunkOptions options,
        string sourceFileName,
        CancellationToken cancellationToken)
    {
        // SR-INT-6 (audit C-09): reject a degenerate configuration here rather
        // than producing quietly useless chunks from it.
        if (!options.TryValidate(out var validationError))
        {
            throw new DocumentConversionException(validationError!, ErrorCategory.ConversionFailure);
        }

        var units = Flatten(document);
        if (units.Count == 0)
        {
            return new ChunkGenerationResult { Chunks = [] };
        }

        var tokenCounts = await _tokenCounter.CountBatchAsync(
            units.Select(u => u.Text).ToList(), cancellationToken);

        var measuredUnits = units
            .Zip(tokenCounts, (unit, count) => new MeasuredUnit(unit.Text, count, unit.BlockId, unit.Location, unit.IsTable))
            .ToList();

        return new ChunkGenerationResult
        {
            Chunks = BuildChunks(measuredUnits, options, sourceFileName),
            Warnings = BuildOversizedTableWarnings(measuredUnits, options)
        };
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
                    units.Add(new AtomicUnit(prefix, null, section.Location, false));
                }

                continue;
            }

            for (var i = 0; i < section.Blocks.Count; i++)
            {
                var block = section.Blocks[i];
                var blockText = MarkdownBlockRenderer.RenderBlock(block);
                units.Add(new AtomicUnit(
                    i == 0 ? prefix + blockText : blockText,
                    block.BlockId,
                    section.Location,
                    block is TableBlock));
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

    // SR-INT-7 (audit C-08). A table too big for the configured chunk size is
    // kept intact in its own chunk rather than split - that part already
    // worked - but nothing said so. A consumer with a hard context limit needs
    // to know the chunk exceeds it, because "the table survived" and "the
    // chunk is usable downstream" are different claims.
    //
    // Warning, not Error: no content was lost. Only tables are reported; an
    // oversized paragraph is not a structural hazard in the same way, and
    // WarningCode.TableExceedsChunkSize would be the wrong label for it.
    private static List<ExtractionWarning> BuildOversizedTableWarnings(
        IReadOnlyList<MeasuredUnit> units,
        ChunkOptions options)
    {
        var warnings = new List<ExtractionWarning>();

        foreach (var unit in units.Where(u => u.IsTable && u.TokenCount > options.ChunkSizeTokens))
        {
            warnings.Add(new ExtractionWarning
            {
                Code = WarningCode.TableExceedsChunkSize,
                Severity = WarningSeverity.Warning,
                Message =
                    $"A table needs {unit.TokenCount} tokens, more than the {options.ChunkSizeTokens}-token "
                    + "chunk size. It is kept whole in a chunk of its own rather than split, so that chunk "
                    + "is larger than the configured size and may exceed a provider's limit.",
                BlockId = unit.BlockId,
                Location = unit.Location,
                Details = new Dictionary<string, string>
                {
                    ["tableTokens"] = unit.TokenCount.ToString(),
                    ["chunkSizeTokens"] = options.ChunkSizeTokens.ToString()
                }
            });
        }

        return warnings;
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

    private sealed record AtomicUnit(string Text, string? BlockId, SourceLocation? Location, bool IsTable);

    private sealed record MeasuredUnit(
        string Text, int TokenCount, string? BlockId, SourceLocation? Location, bool IsTable);
}
