using System.IO.Compression;
using System.Text.Json;
using AI.Document.Converter.Application.Models;

namespace AI.Document.Converter.Application.Services;

// FR-028/046: builds the export ZIP's internal layout - markdown/<name>.md,
// chunks/<name>/chunk_NNN.md (when chunking was requested), and
// metadata/<name>.json for every successfully converted item. No interface -
// this is pure file-copying/archiving logic over already-materialized files,
// with nothing worth mocking (docs/15-IMPLEMENTATION-PLAN.md lists it as a
// plain class, not behind IExportService).
public sealed class ZipPackageBuilder
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<int> BuildAsync(
        IReadOnlyList<ExportItem> items, string zipDestinationPath, CancellationToken cancellationToken)
    {
        var exportedCount = 0;

        using var zipStream = new FileStream(zipDestinationPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        foreach (var item in items)
        {
            var baseName = Path.GetFileNameWithoutExtension(item.SourceFilePath);

            await AddFileEntryAsync(archive, $"markdown/{baseName}.md", item.ConversionResult.OutputPath!, cancellationToken);

            if (item.ChunkResult is { Success: true, OutputPath: not null } chunkResult)
            {
                foreach (var chunkFilePath in Directory.GetFiles(chunkResult.OutputPath, "chunk_*.md"))
                {
                    await AddFileEntryAsync(
                        archive, $"chunks/{baseName}/{Path.GetFileName(chunkFilePath)}", chunkFilePath, cancellationToken);
                }
            }

            await AddMetadataEntryAsync(archive, $"metadata/{baseName}.json", item, cancellationToken);

            exportedCount++;
        }

        return exportedCount;
    }

    private static async Task AddFileEntryAsync(
        ZipArchive archive, string entryName, string sourceFilePath, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var fileStream = File.OpenRead(sourceFilePath);
        await fileStream.CopyToAsync(entryStream, cancellationToken);
    }

    private static async Task AddMetadataEntryAsync(
        ZipArchive archive, string entryName, ExportItem item, CancellationToken cancellationToken)
    {
        var metadata = item.ConversionResult.Metadata!;
        var document = new ExportMetadataDocument
        {
            SourceFileName = Path.GetFileName(item.SourceFilePath),
            FileType = metadata.FileType.ToString(),
            CreatedDate = metadata.CreatedDate,
            ConvertedDate = metadata.ConvertedDate,
            PageCount = metadata.PageCount,
            SlideCount = metadata.SlideCount,
            SheetCount = metadata.SheetCount,
            Author = metadata.Author,
            ConversionSuccess = item.ConversionResult.Success,
            ConversionErrorMessage = item.ConversionResult.ErrorMessage,
            Tokens = item.ConversionResult.Tokens,
            ChunkCount = item.ChunkResult?.ChunkCount
        };

        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, document, MetadataJsonOptions, cancellationToken);
    }
}
