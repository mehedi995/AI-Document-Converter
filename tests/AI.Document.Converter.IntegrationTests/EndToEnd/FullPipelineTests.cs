using System.IO.Compression;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Docx;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Excel;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.DocumentProcessing.PowerPoint;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.FileSystem;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.EndToEnd;

// docs/16-TEST-STRATEGY.md Section 1 ("End-to-End... run as one integration
// test suite... tests/AI.Document.Converter.IntegrationTests/EndToEnd")
// promised this suite from Gate 4 onward but it was never actually built -
// found and closed during the Phase 11 (Testing) review. Runs the full
// Import -> Convert -> Chunk -> Export chain, through the real bundled
// Python engine, once per supported format (FR-001-005/012/018-021/027-028).
public class FullPipelineTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly IImportService _importService;
    private readonly ConversionService _conversionService;
    private readonly ExportService _exportService;

    public FullPipelineTests()
    {
        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(File.Exists(enginePath), $"Bundled Python engine not found at '{enginePath}'.");

        var pythonEngineClient = new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(new AppSettings { PythonExecutablePath = enginePath }),
            NullLogger<PythonEngineClient>.Instance);

        _importService = new ImportService(new FileSizeReader());

        var processorResolver = new DocumentProcessorResolver(
        [
            new TextDocumentProcessor(),
            new PdfDocumentProcessor(pythonEngineClient),
            new DocxDocumentProcessor(pythonEngineClient),
            new ExcelDocumentProcessor(pythonEngineClient),
            new PowerPointDocumentProcessor(pythonEngineClient)
        ]);

        _conversionService = new ConversionService(
            processorResolver,
            new MarkdownGenerator(),
            new TokenEstimator(pythonEngineClient),
            new MarkdownFileWriter(),
            new ChunkGenerator(new TokenCounter(pythonEngineClient)),
            new ChunkFileWriter(),
            NullLogger<ConversionService>.Instance);

        _exportService = new ExportService(new PathValidator(), NullLogger<ExportService>.Instance);

        _workingDirectory = Path.Combine(Path.GetTempPath(), $"ai-doc-converter-e2e-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_workingDirectory);
    }

    [Theory]
    [InlineData("sample.pdf")]
    [InlineData("sample.docx")]
    [InlineData("sample.xlsx")]
    [InlineData("sample.pptx")]
    [InlineData("sample.txt")]
    public async Task FullPipeline_ImportConvertChunkExport_ProducesCompleteZipPackage(string fileName)
    {
        var sourcePath = Path.Combine(RepoPaths.SamplesDirectory(), fileName);
        Assert.True(File.Exists(sourcePath), $"Sample not found at '{sourcePath}'.");

        // Step 1: Import (FR-001-005) - the file must be accepted, not rejected.
        var settings = new AppSettings { MaxBatchFiles = 500, MaxBatchSizeBytes = 5_368_709_120 };
        var importResult = _importService.Import([sourcePath], 0, 0, settings);

        Assert.Single(importResult.AcceptedFiles);
        Assert.Empty(importResult.RejectedFiles);

        // Step 2: Convert (FR-012) - real extraction, markdown generation, and
        // token estimation against the real bundled engine.
        var outputPathResolver = new OutputPathResolver();
        var conversionResult = await _conversionService.ConvertAsync(
            sourcePath, _workingDirectory, outputPathResolver, CancellationToken.None);

        Assert.True(conversionResult.Success, conversionResult.ErrorMessage);
        Assert.True(File.Exists(conversionResult.OutputPath));
        Assert.NotNull(conversionResult.Metadata);
        Assert.NotNull(conversionResult.Tokens);

        // Step 3: Chunk (FR-018-021) - a deliberately small chunk size so
        // even the tiny sample fixtures actually split into multiple chunks.
        var chunkOptions = new ChunkOptions { ChunkSizeTokens = 20, OverlapTokens = 5 };
        var chunkResult = await _conversionService.GenerateChunksAsync(
            sourcePath, _workingDirectory, chunkOptions, CancellationToken.None);

        Assert.True(chunkResult.Success, chunkResult.ErrorMessage);
        Assert.NotNull(chunkResult.ChunkCount);
        Assert.True(chunkResult.ChunkCount > 0);

        // Step 4: Export (FR-027/028/046) - package both stages' output into
        // one ZIP and verify its actual internal layout.
        var exportItem = new ExportItem
        {
            SourceFilePath = sourcePath,
            ConversionResult = conversionResult,
            ChunkResult = chunkResult
        };
        var zipPath = Path.Combine(_workingDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}.zip");

        var exportResult = await _exportService.ExportBatchAsZipAsync([exportItem], zipPath, CancellationToken.None);

        Assert.True(exportResult.Success, exportResult.ErrorMessage);
        Assert.Equal(1, exportResult.ExportedFileCount);

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName).ToHashSet();

        Assert.Contains($"markdown/{baseName}.md", entryNames);
        Assert.Contains($"metadata/{baseName}.json", entryNames);
        Assert.Contains(entryNames, name => name.StartsWith($"chunks/{baseName}/chunk_", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }
}
