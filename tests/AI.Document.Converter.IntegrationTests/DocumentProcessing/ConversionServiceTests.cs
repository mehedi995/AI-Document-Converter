using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
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
            NullLogger<ConversionService>.Instance);

        _outputDirectory = Path.Combine(Path.GetTempPath(), $"ai-doc-converter-test-{Guid.NewGuid()}");
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

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }
}
