using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AI.Document.Converter.Application.Services;

public sealed class ExportService : IExportService
{
    private readonly IPathValidator _pathValidator;
    private readonly ILogger<ExportService> _logger;

    public ExportService(IPathValidator pathValidator, ILogger<ExportService> logger)
    {
        _pathValidator = pathValidator;
        _logger = logger;
    }

    public async Task<ExportResult> ExportBatchAsZipAsync(
        IReadOnlyList<ExportItem> items, string zipDestinationPath, CancellationToken cancellationToken)
    {
        // SEC-002: validated before any write, same as every other
        // user-supplied path in the app.
        if (!_pathValidator.IsValidFilePath(zipDestinationPath))
        {
            _logger.LogWarning("Rejected export destination path: {ZipPath}", zipDestinationPath);
            return new ExportResult
            {
                Success = false,
                Error = ErrorCategory.OutputFailure,
                ErrorMessage = "The selected export location is not valid."
            };
        }

        // BR-006: a file that never converted successfully has nothing to
        // contribute to the package - not a reason to fail the whole export.
        var exportableItems = items.Where(i => i.ConversionResult.Success).ToList();
        if (exportableItems.Count == 0)
        {
            return new ExportResult
            {
                Success = false,
                Error = ErrorCategory.OutputFailure,
                ErrorMessage = "There are no successfully converted files to export."
            };
        }

        try
        {
            var directory = Path.GetDirectoryName(zipDestinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var exportedCount = await new ZipPackageBuilder().BuildAsync(
                exportableItems, zipDestinationPath, cancellationToken);

            _logger.LogInformation(
                "Exported {Count} file(s) to {ZipPath}", exportedCount, zipDestinationPath);

            return new ExportResult { Success = true, ZipPath = zipDestinationPath, ExportedFileCount = exportedCount };
        }
        catch (OperationCanceledException)
        {
            TryDeletePartialZip(zipDestinationPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to write export ZIP to {ZipPath}", zipDestinationPath);
            TryDeletePartialZip(zipDestinationPath);
            return new ExportResult
            {
                Success = false,
                Error = ErrorCategory.OutputFailure,
                ErrorMessage = "The export package could not be written. Check the destination path and try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error exporting to {ZipPath}", zipDestinationPath);
            TryDeletePartialZip(zipDestinationPath);
            return new ExportResult
            {
                Success = false,
                Error = ErrorCategory.UnexpectedException,
                ErrorMessage = "An unexpected error occurred while creating the export package."
            };
        }
    }

    // Best-effort only - a failed export is already being reported to the
    // user, so a leftover partial ZIP at this point isn't worth surfacing a
    // second error for; it will simply be overwritten on the next attempt.
    private static void TryDeletePartialZip(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
