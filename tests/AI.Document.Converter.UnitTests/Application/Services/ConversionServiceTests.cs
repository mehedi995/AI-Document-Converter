using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using AI.Document.Converter.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

public class ConversionServiceTests
{
    private readonly Mock<IDocumentProcessorResolver> _processorResolver = new();
    private readonly Mock<IDocumentProcessor> _processor = new();
    private readonly Mock<IMarkdownGenerator> _markdownGenerator = new();
    private readonly Mock<ITokenEstimator> _tokenEstimator = new();
    private readonly Mock<IMarkdownFileWriter> _fileWriter = new();
    private readonly Mock<IChunkGenerator> _chunkGenerator = new();
    private readonly Mock<IChunkFileWriter> _chunkFileWriter = new();
    private readonly Mock<IOutputPathResolver> _outputPathResolver = new();
    private readonly ConversionService _service;

    private static readonly DocumentModel SampleDocument = new()
    {
        Metadata = new DocumentMetadata
        {
            SourceFilePath = @"C:\Source\report.pdf",
            FileType = SupportedFileType.Pdf,
            CreatedDate = DateTime.UtcNow,
            ConvertedDate = DateTime.UtcNow
        },
        Sections = [new Section { Blocks = [new ParagraphBlock { Text = "Body" }] }]
    };

    public ConversionServiceTests()
    {
        _service = new ConversionService(
            _processorResolver.Object,
            _markdownGenerator.Object,
            _tokenEstimator.Object,
            _fileWriter.Object,
            _chunkGenerator.Object,
            _chunkFileWriter.Object,
            NullLogger<ConversionService>.Instance);

        _processorResolver.Setup(r => r.Resolve(It.IsAny<string>())).Returns(_processor.Object);
        _outputPathResolver
            .Setup(r => r.ResolveMarkdownOutputPath(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(@"C:\Output\report.md");
    }

    [Fact]
    public async Task ConvertAsync_HappyPath_ReturnsSuccessWithTokensAndOutputPath()
    {
        _processor
            .Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDocument);
        _markdownGenerator.Setup(g => g.Generate(SampleDocument)).Returns("# Body\n");
        var tokens = new TokenEstimate
        {
            OriginalClaudeStyle = 10,
            ConvertedClaudeStyle = 5,
            OriginalGpt4oStyle = 9,
            ConvertedGpt4oStyle = 4
        };
        _tokenEstimator
            .Setup(t => t.EstimateAsync(It.IsAny<string>(), "# Body\n", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);

        var result = await _service.ConvertAsync(
            @"C:\Source\report.pdf", @"C:\Output", _outputPathResolver.Object, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(@"C:\Output\report.md", result.OutputPath);
        Assert.Same(tokens, result.Tokens);
        Assert.Equal(PipelineStage.Converted, result.CompletedStages);
        _fileWriter.Verify(
            w => w.WriteAsync(@"C:\Output\report.md", "# Body\n", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConvertAsync_ProcessorThrowsDocumentConversionException_ReturnsCategorizedFailure()
    {
        _processor
            .Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentConversionException("corrupted", ErrorCategory.CorruptedDocument));

        var result = await _service.ConvertAsync(
            @"C:\Source\report.pdf", @"C:\Output", _outputPathResolver.Object, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.CorruptedDocument, result.Error);
        Assert.Equal("corrupted", result.ErrorMessage);
        _fileWriter.Verify(
            w => w.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConvertAsync_UnexpectedException_ReturnsUnexpectedExceptionCategory()
    {
        _processor
            .Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bug"));

        var result = await _service.ConvertAsync(
            @"C:\Source\report.pdf", @"C:\Output", _outputPathResolver.Object, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.UnexpectedException, result.Error);
    }

    [Fact]
    public async Task ConvertAsync_Cancellation_PropagatesInsteadOfReturningAFailureResult()
    {
        _processor
            .Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.ConvertAsync(
            @"C:\Source\report.pdf", @"C:\Output", _outputPathResolver.Object, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateChunksAsync_HappyPath_ReturnsChunkCountAndWritesChunks()
    {
        _processor
            .Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDocument);

        var chunks = new List<DocumentChunk>
        {
            new()
            {
                SequenceNumber = 1,
                SourceFileName = "report.md",
                Content = "Body\n",
                TokenCount = 2,
                OverlapTokens = 0
            }
        };
        _chunkGenerator
            .Setup(c => c.GenerateChunksAsync(
                SampleDocument, It.IsAny<ChunkOptions>(), "report.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkGenerationResult { Chunks = chunks });

        var options = new ChunkOptions { ChunkSizeTokens = 512, OverlapTokens = 50 };
        var result = await _service.GenerateChunksAsync(
            @"C:\Source\report.pdf", @"C:\Output", options, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(PipelineStage.Chunked, result.CompletedStages);
        _chunkFileWriter.Verify(
            w => w.WriteAsync(
                Path.Combine(@"C:\Output", "chunks", "report"), chunks, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateChunksAsync_ProcessorThrowsDocumentConversionException_ReturnsCategorizedFailure()
    {
        _processor
            .Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentConversionException("locked", ErrorCategory.FileLocked));

        var options = new ChunkOptions { ChunkSizeTokens = 512, OverlapTokens = 50 };
        var result = await _service.GenerateChunksAsync(
            @"C:\Source\report.pdf", @"C:\Output", options, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.FileLocked, result.Error);
        _chunkFileWriter.Verify(
            w => w.WriteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<DocumentChunk>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
