using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

// The token counter is mocked to return one "token" per character - a simple,
// fully predictable stand-in for the real tiktoken counts (already covered by
// a real end-to-end integration test), so these tests can construct exact
// boundary conditions (an oversized unit, a forced overflow) just by
// controlling text length.
public class ChunkGeneratorTests
{
    private readonly Mock<ITokenCounter> _tokenCounter = new();
    private readonly ChunkGenerator _generator;

    public ChunkGeneratorTests()
    {
        _generator = new ChunkGenerator(_tokenCounter.Object);

        _tokenCounter
            .Setup(c => c.CountBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(t => t.Length).ToList());
    }

    private static DocumentModel DocumentWith(params Section[] sections) => new()
    {
        Metadata = new DocumentMetadata
        {
            SourceFilePath = "sample.pdf",
            FileType = SupportedFileType.Pdf,
            CreatedDate = DateTime.UtcNow,
            ConvertedDate = DateTime.UtcNow
        },
        Sections = [.. sections]
    };

    [Fact]
    public async Task GenerateChunksAsync_DocumentSmallerThanChunkSize_ProducesExactlyOneChunk()
    {
        var document = DocumentWith(new Section
        {
            Heading = "Title",
            Blocks = [new ParagraphBlock { Text = "Short body." }]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 1000, OverlapTokens = 50 };

        var chunks = (await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None)).Chunks;

        Assert.Single(chunks);
        Assert.Equal(1, chunks[0].SequenceNumber);
        Assert.Equal(0, chunks[0].OverlapTokens);
        Assert.Contains("Title", chunks[0].Content);
        Assert.Contains("Short body.", chunks[0].Content);
    }

    [Fact]
    public async Task GenerateChunksAsync_TableLargerThanChunkSize_StaysIntactInItsOwnChunk()
    {
        // A table whose rendered Markdown is deliberately longer (in "tokens",
        // i.e. characters under the mock) than the configured chunk size.
        var hugeRow = Enumerable.Repeat("data-cell-value", 20).ToList();
        var document = DocumentWith(new Section
        {
            Heading = "Data",
            Blocks =
            [
                new TableBlock { Headers = ["A", "B"], Rows = [hugeRow] }
            ]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 20, OverlapTokens = 5 };

        var chunks = (await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None)).Chunks;

        var tableChunk = Assert.Single(chunks.Where(c => c.Content.Contains('|')));
        Assert.Contains("data-cell-value", tableChunk.Content);
        // FR-019/AC-015: every row of the table survives together, not split.
        Assert.Equal(20, tableChunk.Content.Split("data-cell-value").Length - 1);
    }

    // SR-INT-7 (audit C-08). Keeping the table whole is only half the job: a
    // consumer with a hard context limit has to be told the resulting chunk is
    // over the configured size. WarningCode.TableExceedsChunkSize existed but
    // was never emitted by anything.
    [Fact]
    public async Task GenerateChunksAsync_TableLargerThanChunkSize_RaisesWarningAnchoredToTheBlock()
    {
        var hugeRow = Enumerable.Repeat("data-cell-value", 20).ToList();
        var document = DocumentWith(new Section
        {
            Heading = "Data",
            Location = new SourceLocation { SheetName = "Ledger" },
            Blocks =
            [
                new TableBlock { BlockId = "s1-b1", Headers = ["A", "B"], Rows = [hugeRow] }
            ]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 20, OverlapTokens = 5 };

        var result = await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(WarningCode.TableExceedsChunkSize, warning.Code);
        // No content was lost, so this must not escalate to Error and flip the
        // job into "completed with warnings".
        Assert.Equal(WarningSeverity.Warning, warning.Severity);
        // The block ID is what makes the warning actionable - it points at the
        // specific table, not just "somewhere in this document".
        Assert.Equal("s1-b1", warning.BlockId);
        Assert.Equal("Ledger", warning.Location?.SheetName);
        Assert.Equal("20", warning.Details?["chunkSizeTokens"]);
    }

    [Fact]
    public async Task GenerateChunksAsync_TableWithinChunkSize_RaisesNoWarning()
    {
        var document = DocumentWith(new Section
        {
            Heading = "Data",
            Blocks = [new TableBlock { BlockId = "s1-b1", Headers = ["A", "B"], Rows = [["x", "y"]] }]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 5000, OverlapTokens = 50 };

        var result = await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None);

        Assert.Empty(result.Warnings);
    }

    // An oversized *paragraph* is not a structural hazard in the way a table
    // is, and TableExceedsChunkSize would be the wrong label for it.
    [Fact]
    public async Task GenerateChunksAsync_OversizedParagraph_DoesNotRaiseTableWarning()
    {
        var document = DocumentWith(new Section
        {
            Heading = "Prose",
            Blocks = [new ParagraphBlock { BlockId = "s1-b1", Text = new string('x', 500) }]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 20, OverlapTokens = 5 };

        var result = await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None);

        Assert.DoesNotContain(result.Warnings, w => w.Code == WarningCode.TableExceedsChunkSize);
    }

    [Fact]
    public async Task GenerateChunksAsync_HeadingAndFirstBlock_NeverSplitAcrossChunkBoundary()
    {
        // The heading is padded with the first block deliberately large enough
        // that, if they were treated as separate units, the chunker would
        // otherwise be tempted to split them.
        var document = DocumentWith(new Section
        {
            Heading = "Section Heading",
            Blocks =
            [
                new ParagraphBlock { Text = new string('a', 30) },
                new ParagraphBlock { Text = new string('b', 30) }
            ]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 40, OverlapTokens = 0 };

        var chunks = (await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None)).Chunks;

        var firstChunkWithHeading = Assert.Single(chunks.Where(c => c.Content.Contains("Section Heading")));
        Assert.Contains(new string('a', 30), firstChunkWithHeading.Content);
    }

    [Fact]
    public async Task GenerateChunksAsync_MultipleUnitsExceedingChunkSize_ProducesOverlapBetweenChunks()
    {
        var document = DocumentWith(new Section
        {
            Blocks =
            [
                new ParagraphBlock { Text = new string('a', 20) },
                new ParagraphBlock { Text = new string('b', 20) },
                new ParagraphBlock { Text = new string('c', 20) }
            ]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 25, OverlapTokens = 15 };

        var chunks = (await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None)).Chunks;

        Assert.True(chunks.Count > 1);
        // The second chunk carries overlap from the tail of the first.
        Assert.True(chunks[1].OverlapTokens > 0);
        Assert.Contains(new string('a', 20), chunks[0].Content);
        Assert.Contains(new string('a', 20), chunks[1].Content); // overlapped in
        Assert.Contains(new string('b', 20), chunks[1].Content);
    }

    [Fact]
    public async Task GenerateChunksAsync_SequenceNumbersAreContiguousStartingAtOne()
    {
        var document = DocumentWith(new Section
        {
            Blocks =
            [
                new ParagraphBlock { Text = new string('a', 20) },
                new ParagraphBlock { Text = new string('b', 20) },
                new ParagraphBlock { Text = new string('c', 20) }
            ]
        });
        var options = new ChunkOptions { ChunkSizeTokens = 15, OverlapTokens = 0 };

        var chunks = (await _generator.GenerateChunksAsync(document, options, "doc.md", CancellationToken.None)).Chunks;

        Assert.Equal(Enumerable.Range(1, chunks.Count), chunks.Select(c => c.SequenceNumber));
    }

    [Fact]
    public async Task GenerateChunksAsync_EveryChunkRecordsTheSourceFileName()
    {
        var document = DocumentWith(new Section { Blocks = [new ParagraphBlock { Text = "Body" }] });
        var options = new ChunkOptions { ChunkSizeTokens = 500, OverlapTokens = 0 };

        var chunks = (await _generator.GenerateChunksAsync(document, options, "report.md", CancellationToken.None)).Chunks;

        Assert.All(chunks, c => Assert.Equal("report.md", c.SourceFileName));
    }
}
