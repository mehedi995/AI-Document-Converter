using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Exceptions;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Docx;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Excel;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.DocumentProcessing.PowerPoint;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.DocumentProcessing;

// Exercises every IDocumentProcessor against the real fixtures in samples/,
// through the real bundled Python engine (ADR-001) - the extraction logic's
// correctness genuinely is the Python library's behavior here, so mocking it
// would test nothing (docs/16-TEST-STRATEGY.md).
public class DocumentProcessingTests
{
    private readonly IPythonEngineClient _pythonEngineClient;

    public DocumentProcessingTests()
    {
        RequireBundledEngine();

        var settings = new AppSettings { PythonExecutablePath = RepoPaths.BundledPythonEnginePath() };
        _pythonEngineClient = new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(settings),
            NullLogger<PythonEngineClient>.Instance);
    }

    private static void RequireBundledEngine()
    {
        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(
            File.Exists(enginePath),
            $"Bundled Python engine not found at '{enginePath}'. Run scripts/build-python-engine.ps1 first.");
    }

    private static string SamplePath(string fileName) => Path.Combine(RepoPaths.SamplesDirectory(), fileName);

    [Fact]
    public async Task PdfDocumentProcessor_ExtractsSample_ProducesExpectedStructure()
    {
        var processor = new PdfDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("sample.pdf"), CancellationToken.None);

        Assert.Equal(SupportedFileType.Pdf, document.Metadata.FileType);
        Assert.Equal(3, document.Metadata.PageCount);
        Assert.Equal(3, document.Sections.Count);

        Assert.Equal("Sample PDF Document", document.Sections[0].Heading);
        var table = Assert.IsType<TableBlock>(
            document.Sections[0].Blocks.Single(b => b is TableBlock));
        Assert.Equal(["Item", "Value"], table.Headers);
        Assert.Equal(2, table.Rows.Count);

        // FR-043: the deliberately blank third page is marked, not omitted.
        var unextractable = Assert.IsType<UnextractableTextBlock>(document.Sections[2].Blocks.Single());
        Assert.Equal(ExtractionMethod.PlaceholderNoText, unextractable.Reason);
    }

    [Fact]
    public async Task DocxDocumentProcessor_ExtractsSample_ProducesExpectedStructure()
    {
        var processor = new DocxDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("sample.docx"), CancellationToken.None);

        Assert.Equal(SupportedFileType.Docx, document.Metadata.FileType);
        Assert.Equal(3, document.Sections.Count);

        Assert.Equal("Key Points", document.Sections[1].Heading);
        var list = Assert.IsType<ListBlock>(document.Sections[1].Blocks.Single());
        Assert.Equal(["First point", "Second point", "Third point"], list.Items);

        var referenceSection = document.Sections[2];
        var table = Assert.IsType<TableBlock>(referenceSection.Blocks.OfType<TableBlock>().Single());
        Assert.Equal(["Item", "Value"], table.Headers);

        // FR-007: the hyperlink is preserved inline as Markdown link syntax,
        // not duplicated as a separate block (see the fix during Phase 3).
        var paragraph = Assert.IsType<ParagraphBlock>(referenceSection.Blocks.OfType<ParagraphBlock>().Single());
        Assert.Contains("[example reference](https://example.com/reference)", paragraph.Text);
    }

    [Fact]
    public async Task ExcelDocumentProcessor_ExtractsSample_HandlesFormulasMergesAndSummarization()
    {
        var processor = new ExcelDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("sample.xlsx"), CancellationToken.None);

        Assert.Equal(SupportedFileType.Xlsx, document.Metadata.FileType);
        Assert.Equal(2, document.Metadata.SheetCount);
        Assert.Equal(2, document.Sections.Count);

        var dataTable = Assert.IsType<TableBlock>(document.Sections[0].Blocks.Single());
        // FR-042: a formula cell with no cached value renders empty, never the
        // formula text itself.
        Assert.DoesNotContain(dataTable.Rows, row => row.Any(cell => cell.StartsWith('=')));
        // FR-042: the merged cell's value is repeated across both spanned columns.
        // Row index 3 (not 2): row 4 in the sheet is blank, sitting between the
        // data rows and the merged row 5.
        Assert.Equal("Merged note spanning two columns", dataTable.Rows[3][0]);
        Assert.Equal("Merged note spanning two columns", dataTable.Rows[3][1]);

        // FR-041: the oversized sheet is summarized, with a note and a
        // truncated row count, not the full 251 rows.
        var largeSection = document.Sections[1];
        var note = Assert.IsType<ParagraphBlock>(largeSection.Blocks.OfType<ParagraphBlock>().Single());
        Assert.Contains("summarization", note.Text);
        var largeTable = Assert.IsType<TableBlock>(largeSection.Blocks.OfType<TableBlock>().Single());
        Assert.True(largeTable.Rows.Count < 250);
    }

    [Fact]
    public async Task PowerPointDocumentProcessor_ExtractsSample_ProducesExpectedStructure()
    {
        var processor = new PowerPointDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("sample.pptx"), CancellationToken.None);

        Assert.Equal(SupportedFileType.Pptx, document.Metadata.FileType);
        Assert.Equal(2, document.Metadata.SlideCount);
        Assert.Equal(2, document.Sections.Count);

        Assert.Equal("Sample Presentation", document.Sections[0].Heading);
        var list = Assert.IsType<ListBlock>(document.Sections[0].Blocks.OfType<ListBlock>().Single());
        Assert.Equal(["First bullet point", "Second bullet point"], list.Items);

        var notes = Assert.IsType<ParagraphBlock>(document.Sections[0].Blocks.OfType<ParagraphBlock>().Single());
        Assert.Contains("Speaker notes for the sample slide.", notes.Text);
    }

    [Fact]
    public async Task PdfDocumentProcessor_MissingFile_ThrowsFileNotFoundCategory()
    {
        var processor = new PdfDocumentProcessor(_pythonEngineClient);

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => processor.ExtractAsync(SamplePath("does-not-exist.pdf"), CancellationToken.None));

        Assert.Equal(ErrorCategory.FileNotFound, exception.Category);
    }

    [Fact]
    public async Task TextDocumentProcessor_ExtractsSample_NoPythonInvolved()
    {
        var processor = new TextDocumentProcessor();

        var document = await processor.ExtractAsync(SamplePath("sample.txt"), CancellationToken.None);

        Assert.Equal(SupportedFileType.Txt, document.Metadata.FileType);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Sections.Single().Blocks.Single());
        Assert.Contains("Sample Text Document", paragraph.Text);
    }

    [Fact]
    public void DocumentProcessorResolver_ResolvesCorrectProcessorPerExtension()
    {
        var processors = new IDocumentProcessor[]
        {
            new TextDocumentProcessor(),
            new PdfDocumentProcessor(_pythonEngineClient),
            new DocxDocumentProcessor(_pythonEngineClient),
            new ExcelDocumentProcessor(_pythonEngineClient),
            new PowerPointDocumentProcessor(_pythonEngineClient)
        };
        var resolver = new DocumentProcessorResolver(processors);

        Assert.IsType<PdfDocumentProcessor>(resolver.Resolve(SamplePath("sample.pdf")));
        Assert.IsType<DocxDocumentProcessor>(resolver.Resolve(SamplePath("sample.docx")));
        Assert.IsType<ExcelDocumentProcessor>(resolver.Resolve(SamplePath("sample.xlsx")));
        Assert.IsType<PowerPointDocumentProcessor>(resolver.Resolve(SamplePath("sample.pptx")));
        Assert.IsType<TextDocumentProcessor>(resolver.Resolve(SamplePath("sample.txt")));

        var exception = Assert.Throws<DocumentConversionException>(
            () => resolver.Resolve(SamplePath("sample.html")));
        Assert.Equal(ErrorCategory.UnsupportedFile, exception.Category);
    }
}
