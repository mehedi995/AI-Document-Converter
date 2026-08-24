using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

// docs/15-IMPLEMENTATION-PLAN.md's stated testing approach for Phase 10:
// unit tests cover export-path validation rejection here; a real ZIP with a
// real internal layout is exercised as an integration test instead
// (ZipPackageBuilder has no interface to mock - it is pure file-copying
// logic over already-materialized files).
public class ExportServiceTests
{
    private readonly Mock<IPathValidator> _pathValidator = new();
    private readonly ExportService _service;

    public ExportServiceTests()
    {
        _service = new ExportService(_pathValidator.Object, NullLogger<ExportService>.Instance);
    }

    private static ExportItem SuccessfulItem() => new()
    {
        SourceFilePath = @"C:\Docs\sample.docx",
        ConversionResult = new ConversionResult { Success = true, OutputPath = @"C:\Out\sample.md" }
    };

    [Fact]
    public async Task ExportBatchAsZipAsync_InvalidDestinationPath_RejectsWithOutputFailure()
    {
        // SEC-002: rejected before any write is attempted, regardless of
        // whether there are exportable items.
        _pathValidator.Setup(v => v.IsValidFilePath(It.IsAny<string>())).Returns(false);

        var result = await _service.ExportBatchAsZipAsync(
            [SuccessfulItem()], @"..\traversal\export.zip", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.OutputFailure, result.Error);
        Assert.Null(result.ZipPath);
    }

    [Fact]
    public async Task ExportBatchAsZipAsync_NoSuccessfulConversions_ReturnsOutputFailureWithoutWriting()
    {
        _pathValidator.Setup(v => v.IsValidFilePath(It.IsAny<string>())).Returns(true);

        var failedItem = new ExportItem
        {
            SourceFilePath = @"C:\Docs\broken.docx",
            ConversionResult = new ConversionResult { Success = false, Error = ErrorCategory.CorruptedDocument }
        };

        var zipPath = Path.Combine(Path.GetTempPath(), $"export-test-{Guid.NewGuid()}.zip");

        var result = await _service.ExportBatchAsZipAsync([failedItem], zipPath, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.OutputFailure, result.Error);
        Assert.False(File.Exists(zipPath));
    }
}
