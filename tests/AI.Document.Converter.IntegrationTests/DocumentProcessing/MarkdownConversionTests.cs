using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Docx;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.DocumentProcessing;

// Extract (Phase 3) -> Generate Markdown (Phase 4), end to end against the
// real bundled engine and real fixtures - not just isolated units.
public class MarkdownConversionTests
{
    private readonly IPythonEngineClient _pythonEngineClient;
    private readonly MarkdownGenerator _markdownGenerator = new();

    public MarkdownConversionTests()
    {
        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(File.Exists(enginePath), $"Bundled Python engine not found at '{enginePath}'.");

        var settings = new AppSettings { PythonExecutablePath = enginePath };
        _pythonEngineClient = new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(settings),
            NullLogger<PythonEngineClient>.Instance);
    }

    [Fact]
    public async Task PdfSample_ExtractThenGenerate_ProducesWellFormedMarkdown()
    {
        var processor = new PdfDocumentProcessor(_pythonEngineClient);
        var document = await processor.ExtractAsync(
            Path.Combine(RepoPaths.SamplesDirectory(), "sample.pdf"), CancellationToken.None);

        var markdown = _markdownGenerator.Generate(document);

        Assert.StartsWith("---", markdown);
        Assert.Contains("file_type: pdf", markdown);
        Assert.Contains("# Sample PDF Document", markdown);
        Assert.Contains("| Item | Value |", markdown);
        Assert.Contains("*Page 2*", markdown);
        Assert.Contains("no extractable text", markdown);
    }

    [Fact]
    public async Task DocxSample_ExtractThenGenerate_PreservesInlineLinkSyntax()
    {
        var processor = new DocxDocumentProcessor(_pythonEngineClient);
        var document = await processor.ExtractAsync(
            Path.Combine(RepoPaths.SamplesDirectory(), "sample.docx"), CancellationToken.None);

        var markdown = _markdownGenerator.Generate(document);

        Assert.Contains("[example reference](https://example.com/reference)", markdown);
        Assert.Contains("- First point", markdown);
    }
}
