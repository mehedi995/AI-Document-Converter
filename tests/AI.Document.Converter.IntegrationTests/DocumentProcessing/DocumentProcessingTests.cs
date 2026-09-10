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

    // SR-INT-1 (supersedes the legacy FR-041 summarization default). Before
    // this, the 251-row sheet came back as 5 rows reported as a plain success -
    // undisclosed data loss (SaaS audit C-01). Faithful mode is now the default
    // and must return the sheet in full.
    [Fact]
    public async Task ExcelDocumentProcessor_ExtractsSample_ReturnsEveryRowAndPreservesFormulasAndMerges()
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

        // The large sheet is now returned COMPLETE - 251 sheet rows = 1 header
        // row + 250 data rows - with no explanatory "summarization" prose block
        // standing in for the missing content.
        var largeSection = document.Sections[1];
        Assert.Empty(largeSection.Blocks.OfType<ParagraphBlock>());
        var largeTable = Assert.IsType<TableBlock>(largeSection.Blocks.OfType<TableBlock>().Single());
        Assert.Equal(250, largeTable.Rows.Count);

        // A complete extraction must not claim anything was lost.
        Assert.DoesNotContain(document.Warnings, w => w.Code == WarningCode.SheetTruncated);
        Assert.False(document.HasUnrecoveredContent);
    }

    // SR-INT-1, the path that had no .NET coverage at all until now: summary
    // mode was reachable from Python but not from the host, so its
    // Error-severity warning was proven only at the Python level.
    //
    // This is the behaviour that caused SaaS audit C-01 when it was the SILENT
    // default. It is still available, because previewing a huge spreadsheet is
    // genuinely useful - but it now has to announce itself.
    [Fact]
    public async Task ExcelDocumentProcessor_SummaryMode_SamplesTheSheetAndSaysSoAtErrorSeverity()
    {
        var processor = new ExcelDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(
            SamplePath("sample.xlsx"), ExtractionMode.Summary, CancellationToken.None);

        // The 250-row sheet comes back sampled, not complete.
        var largeTable = document.Sections[1].Blocks.OfType<TableBlock>().Single();
        Assert.True(
            largeTable.Rows.Count < 250,
            $"Summary mode returned {largeTable.Rows.Count} rows; it is supposed to sample.");

        var truncation = Assert.Single(
            document.Warnings.Where(w => w.Code == WarningCode.SheetTruncated));

        // Error, not Warning: this is content that was NOT recovered, and the
        // severity is what forces the job to "completed with warnings" rather
        // than plain success (SR-INT-1, SR-INT-3).
        Assert.Equal(WarningSeverity.Error, truncation.Severity);
        Assert.True(document.HasUnrecoveredContent);
    }

    // A sample must never read as a complete extraction, in the output itself
    // and not only in the warnings channel.
    [Fact]
    public async Task ExcelDocumentProcessor_SummaryMode_LabelsTheOutputAsIncomplete()
    {
        var processor = new ExcelDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(
            SamplePath("sample.xlsx"), ExtractionMode.Summary, CancellationToken.None);

        var prose = document.Sections[1].Blocks.OfType<ParagraphBlock>()
            .Select(block => block.Text)
            .ToList();

        Assert.Contains(prose, text => text.Contains("INCOMPLETE", StringComparison.Ordinal));
    }

    // Faithful is the default, and asking for it explicitly must not change
    // anything - otherwise the default and the named mode could drift apart.
    [Fact]
    public async Task ExcelDocumentProcessor_ExplicitFaithfulMode_MatchesTheDefault()
    {
        var processor = new ExcelDocumentProcessor(_pythonEngineClient);

        var byDefault = await processor.ExtractAsync(SamplePath("sample.xlsx"), CancellationToken.None);
        var explicitly = await processor.ExtractAsync(
            SamplePath("sample.xlsx"), ExtractionMode.Faithful, CancellationToken.None);

        Assert.Equal(
            byDefault.Sections[1].Blocks.OfType<TableBlock>().Single().Rows.Count,
            explicitly.Sections[1].Blocks.OfType<TableBlock>().Single().Rows.Count);

        Assert.False(explicitly.HasUnrecoveredContent);
    }

    // The engine ignores a mode it does not recognise for a format, so without
    // a host-side guard a caller asking a PDF for a summary would get a
    // faithful extraction labelled as a summary. Refusing is the point.
    [Fact]
    public async Task PdfDocumentProcessor_SummaryMode_IsRefusedRatherThanQuietlyIgnored()
    {
        var processor = new PdfDocumentProcessor(_pythonEngineClient);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            processor.ExtractAsync(SamplePath("sample.pdf"), ExtractionMode.Summary, CancellationToken.None));
    }

    // TextDocumentProcessor never touches the engine, so it takes the
    // interface's default implementation. That default must refuse too.
    [Fact]
    public async Task TextDocumentProcessor_SummaryMode_IsRefused()
    {
        // Typed as the interface deliberately: a default interface method is
        // not visible on the concrete class, and every real caller reaches
        // processors through IDocumentProcessor from the resolver anyway.
        IDocumentProcessor processor = new TextDocumentProcessor();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            processor.ExtractAsync(SamplePath("sample.txt"), ExtractionMode.Summary, CancellationToken.None));
    }

    // Model v2 contract (SaaS audit B-04/B-06): the versions and the stable
    // block IDs must survive the Python -> JSON -> C# round trip, because
    // chunks, warnings and source highlights all anchor to them.
    [Fact]
    public async Task ExcelDocumentProcessor_ExtractsSample_PopulatesModelV2ContractFields()
    {
        var processor = new ExcelDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("sample.xlsx"), CancellationToken.None);

        Assert.Equal("2.0", document.ModelVersion);
        Assert.False(string.IsNullOrWhiteSpace(document.EngineVersion));

        var blockIds = document.Sections
            .SelectMany(section => section.Blocks)
            .Select(block => block.BlockId)
            .ToList();

        Assert.All(blockIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(blockIds.Count, blockIds.Distinct().Count());
    }

    // SR-INT-3 (SaaS audit C-03): a page with no extractable text used to
    // produce only an inline placeholder, leaving the job to report plain
    // success. It must now also raise an Error-severity warning, which is what
    // forces "completed with warnings" instead.
    [Fact]
    public async Task PdfDocumentProcessor_PageWithNoText_RaisesErrorSeverityWarning()
    {
        var processor = new PdfDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("sample.pdf"), CancellationToken.None);

        var warning = Assert.Single(
            document.Warnings.Where(w => w.Code == WarningCode.NoExtractableText));

        Assert.Equal(WarningSeverity.Error, warning.Severity);
        Assert.True(document.HasUnrecoveredContent);

        // The warning has to say WHAT was not recovered, not merely that
        // something wasn't.
        Assert.NotNull(warning.Details);
        Assert.Equal("1", warning.Details!["pagesWithoutTextCount"]);
        Assert.Equal("3", warning.Details!["totalPages"]);
    }

    // SR-INT-4: an omitted image must be visible as a warning, not only as an
    // inline placeholder a reader might scroll past.
    [Fact]
    public async Task PdfDocumentProcessor_OmittedImages_AreReportedAsInfoWarning()
    {
        var processor = new PdfDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("image-sample.pdf"), CancellationToken.None);

        var warning = Assert.Single(
            document.Warnings.Where(w => w.Code == WarningCode.ImageOmitted));

        // Info, not Error: the image is marked in place and no *text* was lost,
        // so this must not flip the job into "completed with warnings".
        Assert.Equal(WarningSeverity.Info, warning.Severity);
        Assert.False(document.HasUnrecoveredContent);
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

    // AC-027/FR-039: a real embedded image, driven through the real bundled
    // engine per format - PPTX's detection already worked, but had never
    // actually been verified against a real fixture either; PDF and DOCX's
    // detection did not exist at all until Phase 11 closed this gap.
    [Fact]
    public async Task PdfDocumentProcessor_ExtractsImageSample_ProducesImagePlaceholder()
    {
        var processor = new PdfDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("image-sample.pdf"), CancellationToken.None);

        Assert.Contains(
            document.Sections.SelectMany(s => s.Blocks), b => b is ImagePlaceholderBlock);
    }

    [Fact]
    public async Task DocxDocumentProcessor_ExtractsImageSample_ProducesImagePlaceholder()
    {
        var processor = new DocxDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("image-sample.docx"), CancellationToken.None);

        Assert.Contains(
            document.Sections.SelectMany(s => s.Blocks), b => b is ImagePlaceholderBlock);
    }

    [Fact]
    public async Task PowerPointDocumentProcessor_ExtractsImageSample_ProducesImagePlaceholder()
    {
        var processor = new PowerPointDocumentProcessor(_pythonEngineClient);

        var document = await processor.ExtractAsync(SamplePath("image-sample.pptx"), CancellationToken.None);

        Assert.Contains(
            document.Sections.SelectMany(s => s.Blocks), b => b is ImagePlaceholderBlock);
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
    public async Task TextDocumentProcessor_Utf8WithBom_StripsBomAndDecodesCorrectly()
    {
        // FR-040: sample.txt (generated without a BOM) already covers the
        // "without BOM" half of this requirement - this covers the other
        // half explicitly, since DetectEncoding's BOM-sniffing branch is
        // otherwise never exercised by any existing fixture.
        var path = Path.Combine(Path.GetTempPath(), $"bom-test-{Guid.NewGuid()}.txt");
        var utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = "Text with a BOM."u8.ToArray();
        File.WriteAllBytes(path, [.. utf8Bom, .. content]);

        try
        {
            var processor = new TextDocumentProcessor();
            var document = await processor.ExtractAsync(path, CancellationToken.None);

            var paragraph = Assert.IsType<ParagraphBlock>(document.Sections.Single().Blocks.Single());
            Assert.Equal("Text with a BOM.", paragraph.Text);
            Assert.DoesNotContain('﻿', paragraph.Text);
        }
        finally
        {
            File.Delete(path);
        }
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
