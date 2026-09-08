using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.FileSystem;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.DocumentProcessing;

// The full UC-001 pipeline (extract -> generate Markdown -> estimate tokens ->
// write file) against the real bundled engine and real fixtures - the
// genuine, highest-confidence check for AC-012.
public class ConversionServiceTests : IDisposable
{
    private readonly string _outputDirectory;
    private readonly ConversionService _service;

    public ConversionServiceTests()
    {
        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(File.Exists(enginePath), $"Bundled Python engine not found at '{enginePath}'.");

        var pythonEngineClient = new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(new AppSettings { PythonExecutablePath = enginePath }),
            NullLogger<PythonEngineClient>.Instance);

        var processorResolver = new DocumentProcessorResolver(
        [
            new TextDocumentProcessor(),
            new PdfDocumentProcessor(pythonEngineClient)
        ]);

        _service = new ConversionService(
            processorResolver,
            new MarkdownGenerator(),
            new TokenEstimator(pythonEngineClient),
            new MarkdownFileWriter(),
            new ChunkGenerator(new TokenCounter(pythonEngineClient)),
            new ChunkFileWriter(),
            NullLogger<ConversionService>.Instance);

        _outputDirectory = Path.Combine(Path.GetTempPath(), $"ai-doc-converter-test-{Guid.NewGuid()}");
    }

    // The whole point of the warnings channel is that the CALLER can see it.
    // Warnings were reaching DocumentModel but ConversionResult had nowhere to
    // put them, so they were produced by the engine and then dropped before
    // anyone could act on them - a run with missing content still looked like
    // a plain success to every consumer.
    //
    // Exercises the full path: Python engine -> JSON -> DocumentModel ->
    // ConversionResult, against the real bundled engine.
    [Fact]
    public async Task ConvertAsync_RealPdfWithBlankPage_SurfacesWarningsOnTheResult()
    {
        var sourcePath = Path.Combine(RepoPaths.SamplesDirectory(), "sample.pdf");

        var result = await _service.ConvertAsync(
            sourcePath, _outputDirectory, new OutputPathResolver(), CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);

        var warning = Assert.Single(
            result.Warnings.Where(w => w.Code == WarningCode.NoExtractableText));
        Assert.Equal(WarningSeverity.Error, warning.Severity);

        // Success AND incomplete at the same time - the distinction the batch
        // summary and the SaaS job status both depend on.
        Assert.True(result.HasUnrecoveredContent);
    }

    [Fact]
    public async Task ConvertAsync_RealPdfSample_ProducesFileWithTokenEstimatesAndReduction()
    {
        var sourcePath = Path.Combine(RepoPaths.SamplesDirectory(), "sample.pdf");

        var result = await _service.ConvertAsync(
            sourcePath, _outputDirectory, new OutputPathResolver(), CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        Assert.NotNull(result.Tokens);
        // AC-012: original, converted, and (via the computed property) the
        // reduction percentage must all be present and sane.
        Assert.True(result.Tokens!.OriginalGpt4oStyle > 0);
        Assert.True(result.Tokens.ConvertedGpt4oStyle > 0);
        Assert.True(result.Tokens.OriginalClaudeStyle > 0);
        Assert.True(result.Tokens.ConvertedClaudeStyle > 0);

        var writtenContent = await File.ReadAllTextAsync(result.OutputPath!);
        Assert.StartsWith("---", writtenContent);
        Assert.Contains("Sample PDF Document", writtenContent);
    }

    [Fact]
    public async Task ConvertAsync_SameFileTwice_OverwritesRatherThanFailing()
    {
        var sourcePath = Path.Combine(RepoPaths.SamplesDirectory(), "sample.txt");
        var outputPathResolver = new OutputPathResolver();

        var first = await _service.ConvertAsync(sourcePath, _outputDirectory, outputPathResolver, CancellationToken.None);
        var second = await _service.ConvertAsync(sourcePath, _outputDirectory, outputPathResolver, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.OutputPath, second.OutputPath);
    }

    [Fact]
    public async Task GenerateChunksAsync_RealPdfSampleWithSmallChunkSize_ProducesMultipleChunkFiles()
    {
        var sourcePath = Path.Combine(RepoPaths.SamplesDirectory(), "sample.pdf");
        // Deliberately tiny so the small sample actually splits into multiple
        // chunks, exercising the boundary/overlap logic for real rather than
        // trivially producing one chunk.
        var options = new ChunkOptions { ChunkSizeTokens = 15, OverlapTokens = 5 };

        var result = await _service.GenerateChunksAsync(sourcePath, _outputDirectory, options, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.ChunkCount);
        Assert.True(result.ChunkCount > 1);

        var chunkFiles = Directory.GetFiles(result.OutputPath!, "chunk_*.md");
        Assert.Equal(result.ChunkCount, chunkFiles.Length);

        var firstChunkContent = await File.ReadAllTextAsync(chunkFiles[0]);
        Assert.StartsWith("---", firstChunkContent);
        Assert.Contains("source:", firstChunkContent);
        Assert.Contains("chunk: 1 of", firstChunkContent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }
}
