using System.IO.Compression;
using System.Text.Json;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.Export;

// AC-021/FR-028/046: a real ZipArchive over real fixture files, asserting
// the actual internal layout - the thing docs/15-IMPLEMENTATION-PLAN.md
// calls out as needing a real ZIP rather than a mock (no Python engine
// involved here; that's exercised elsewhere - Export only ever repackages
// output the pipeline already wrote).
public class ExportServiceIntegrationTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly ExportService _service;

    public ExportServiceIntegrationTests()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), $"ai-doc-converter-export-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_workingDirectory);
        _service = new ExportService(new PathValidator(), NullLogger<ExportService>.Instance);
    }

    [Fact]
    public async Task ExportBatchAsZipAsync_ConvertedAndChunkedFile_ProducesExpectedZipLayout()
    {
        var markdownPath = Path.Combine(_workingDirectory, "report.md");
        await File.WriteAllTextAsync(markdownPath, "# Report\n\nBody text.");

        var chunksDirectory = Path.Combine(_workingDirectory, "chunks", "report");
        Directory.CreateDirectory(chunksDirectory);
        await File.WriteAllTextAsync(Path.Combine(chunksDirectory, "chunk_001.md"), "chunk one");
        await File.WriteAllTextAsync(Path.Combine(chunksDirectory, "chunk_002.md"), "chunk two");

        var conversionResult = new ConversionResult
        {
            Success = true,
            OutputPath = markdownPath,
            Tokens = new TokenEstimate
            {
                OriginalClaudeStyle = 100,
                ConvertedClaudeStyle = 60,
                OriginalGpt4oStyle = 100,
                ConvertedGpt4oStyle = 55
            },
            Metadata = new DocumentMetadata
            {
                SourceFilePath = @"C:\Docs\report.docx",
                FileType = SupportedFileType.Docx,
                CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ConvertedDate = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
                Author = "Jane Doe"
            }
        };
        var chunkResult = new ConversionResult
        {
            Success = true,
            OutputPath = chunksDirectory,
            ChunkCount = 2
        };

        var item = new ExportItem
        {
            SourceFilePath = @"C:\Docs\report.docx",
            ConversionResult = conversionResult,
            ChunkResult = chunkResult
        };

        var zipPath = Path.Combine(_workingDirectory, "export.zip");

        var result = await _service.ExportBatchAsZipAsync([item], zipPath, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, result.ExportedFileCount);
        Assert.True(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName).ToHashSet();

        Assert.Contains("markdown/report.md", entryNames);
        Assert.Contains("chunks/report/chunk_001.md", entryNames);
        Assert.Contains("chunks/report/chunk_002.md", entryNames);
        Assert.Contains("metadata/report.json", entryNames);

        var markdownEntry = archive.GetEntry("markdown/report.md")!;
        using var markdownReader = new StreamReader(markdownEntry.Open());
        Assert.Equal("# Report\n\nBody text.", await markdownReader.ReadToEndAsync());

        var metadataEntry = archive.GetEntry("metadata/report.json")!;
        using var metadataStream = metadataEntry.Open();
        var metadataDocument = await JsonSerializer.DeserializeAsync<JsonDocument>(metadataStream);
        var root = metadataDocument!.RootElement;

        Assert.Equal("report.docx", root.GetProperty("sourceFileName").GetString());
        Assert.Equal("Docx", root.GetProperty("fileType").GetString());
        Assert.Equal("Jane Doe", root.GetProperty("author").GetString());
        Assert.True(root.GetProperty("conversionSuccess").GetBoolean());
        Assert.Equal(2, root.GetProperty("chunkCount").GetInt32());
        Assert.Equal(55, root.GetProperty("tokens").GetProperty("convertedGpt4oStyle").GetInt32());
    }

    [Fact]
    public async Task ExportBatchAsZipAsync_FileNeverChunked_OmitsChunksEntryForThatFile()
    {
        var markdownPath = Path.Combine(_workingDirectory, "no-chunks.md");
        await File.WriteAllTextAsync(markdownPath, "# No Chunks");

        var item = new ExportItem
        {
            SourceFilePath = @"C:\Docs\no-chunks.docx",
            ConversionResult = new ConversionResult
            {
                Success = true,
                OutputPath = markdownPath,
                Metadata = new DocumentMetadata
                {
                    SourceFilePath = @"C:\Docs\no-chunks.docx",
                    FileType = SupportedFileType.Docx,
                    CreatedDate = DateTime.UtcNow,
                    ConvertedDate = DateTime.UtcNow
                }
            }
            // ChunkResult intentionally omitted - Generate Chunks was never run for this file.
        };

        var zipPath = Path.Combine(_workingDirectory, "no-chunks.zip");

        var result = await _service.ExportBatchAsZipAsync([item], zipPath, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Contains("markdown/no-chunks.md", archive.Entries.Select(e => e.FullName));
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("chunks/", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }
}
