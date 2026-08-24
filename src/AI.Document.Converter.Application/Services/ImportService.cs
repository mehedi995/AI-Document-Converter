using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Services;

// FR-005: validates extensions; NFR-013: enforces the batch file-count and
// byte-size ceilings cumulatively against whatever is already in the batch
// (currentBatchFileCount/currentBatchSizeBytes), not just the new candidates.
public sealed class ImportService : IImportService
{
    private readonly IFileSizeReader _fileSizeReader;

    public ImportService(IFileSizeReader fileSizeReader)
    {
        _fileSizeReader = fileSizeReader;
    }

    public ImportResult Import(
        IReadOnlyList<string> filePaths,
        int currentBatchFileCount,
        long currentBatchSizeBytes,
        AppSettings settings)
    {
        var accepted = new List<FileImportItem>();
        var rejected = new List<RejectedFile>();
        var totalFileCount = currentBatchFileCount;
        var totalSizeBytes = currentBatchSizeBytes;
        var excludedByLimit = 0;

        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);

            if (!SupportedFileTypeExtensions.TryFromExtension(extension, out var fileType))
            {
                rejected.Add(new RejectedFile
                {
                    FilePath = filePath,
                    Reason = $"'{fileName}' has an unsupported file type ('{extension}') and was not added."
                });
                continue;
            }

            var sizeBytes = _fileSizeReader.GetFileSizeBytes(filePath);

            var wouldExceedFileCount = totalFileCount + 1 > settings.MaxBatchFiles;
            var wouldExceedByteSize = totalSizeBytes + sizeBytes > settings.MaxBatchSizeBytes;

            if (wouldExceedFileCount || wouldExceedByteSize)
            {
                excludedByLimit++;
                continue;
            }

            accepted.Add(new FileImportItem
            {
                FilePath = filePath,
                FileName = fileName,
                FileType = fileType,
                FileSizeBytes = sizeBytes
            });

            totalFileCount++;
            totalSizeBytes += sizeBytes;
        }

        return new ImportResult
        {
            AcceptedFiles = accepted,
            RejectedFiles = rejected,
            FilesExcludedByBatchLimit = excludedByLimit
        };
    }
}
