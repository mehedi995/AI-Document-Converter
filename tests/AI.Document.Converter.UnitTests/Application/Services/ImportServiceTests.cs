using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

// Uses only synthetic file paths and a mocked IFileSizeReader - no real files
// touched, matching docs/15-IMPLEMENTATION-PLAN.md Section 2's testing approach.
public class ImportServiceTests
{
    private readonly Mock<IFileSizeReader> _fileSizeReader = new();
    private readonly ImportService _service;

    public ImportServiceTests()
    {
        _service = new ImportService(_fileSizeReader.Object);
    }

    private static AppSettings SettingsWithLimits(int maxFiles, long maxBytes) => new()
    {
        MaxBatchFiles = maxFiles,
        MaxBatchSizeBytes = maxBytes
    };

    [Fact]
    public void Import_AllSupportedExtensions_AllAccepted()
    {
        var paths = new[] { @"C:\docs\a.pdf", @"C:\docs\b.docx", @"C:\docs\c.xlsx", @"C:\docs\d.pptx", @"C:\docs\e.txt" };
        _fileSizeReader.Setup(r => r.GetFileSizeBytes(It.IsAny<string>())).Returns(100);

        var result = _service.Import(paths, currentBatchFileCount: 0, currentBatchSizeBytes: 0, SettingsWithLimits(500, long.MaxValue));

        Assert.Equal(5, result.AcceptedFiles.Count);
        Assert.Empty(result.RejectedFiles);
        Assert.Equal(0, result.FilesExcludedByBatchLimit);
    }

    [Fact]
    public void Import_UnsupportedExtension_IsRejectedNotAdded()
    {
        var paths = new[] { @"C:\docs\a.pdf", @"C:\docs\b.html" };
        _fileSizeReader.Setup(r => r.GetFileSizeBytes(It.IsAny<string>())).Returns(100);

        var result = _service.Import(paths, 0, 0, SettingsWithLimits(500, long.MaxValue));

        Assert.Single(result.AcceptedFiles);
        Assert.Equal(SupportedFileType.Pdf, result.AcceptedFiles[0].FileType);
        Assert.Single(result.RejectedFiles);
        Assert.Contains("b.html", result.RejectedFiles[0].Reason);
    }

    [Fact]
    public void Import_ExceedsMaxBatchFiles_ExcludesExcessFilesOnly()
    {
        var paths = new[] { @"C:\docs\a.pdf", @"C:\docs\b.pdf", @"C:\docs\c.pdf" };
        _fileSizeReader.Setup(r => r.GetFileSizeBytes(It.IsAny<string>())).Returns(100);

        var result = _service.Import(paths, currentBatchFileCount: 0, currentBatchSizeBytes: 0, SettingsWithLimits(maxFiles: 2, long.MaxValue));

        Assert.Equal(2, result.AcceptedFiles.Count);
        Assert.Equal(1, result.FilesExcludedByBatchLimit);
    }

    [Fact]
    public void Import_ExceedsMaxBatchSizeBytes_ExcludesExcessFilesOnly()
    {
        var paths = new[] { @"C:\docs\a.pdf", @"C:\docs\b.pdf" };
        _fileSizeReader.Setup(r => r.GetFileSizeBytes(It.IsAny<string>())).Returns(600);

        var result = _service.Import(paths, 0, 0, SettingsWithLimits(500, maxBytes: 1000));

        Assert.Single(result.AcceptedFiles);
        Assert.Equal(1, result.FilesExcludedByBatchLimit);
    }

    [Fact]
    public void Import_RespectsExistingBatchTotalsCumulatively()
    {
        var paths = new[] { @"C:\docs\a.pdf" };
        _fileSizeReader.Setup(r => r.GetFileSizeBytes(It.IsAny<string>())).Returns(100);

        // Already at the file-count ceiling from a previous import call.
        var result = _service.Import(paths, currentBatchFileCount: 500, currentBatchSizeBytes: 0, SettingsWithLimits(maxFiles: 500, long.MaxValue));

        Assert.Empty(result.AcceptedFiles);
        Assert.Equal(1, result.FilesExcludedByBatchLimit);
    }

    [Fact]
    public void Import_EmptyList_ReturnsEmptyResult()
    {
        var result = _service.Import([], 0, 0, SettingsWithLimits(500, long.MaxValue));

        Assert.Empty(result.AcceptedFiles);
        Assert.Empty(result.RejectedFiles);
        Assert.Equal(0, result.FilesExcludedByBatchLimit);
    }
}
