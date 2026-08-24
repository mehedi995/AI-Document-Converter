using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

public class SettingsServiceTests
{
    private readonly Mock<ISettingsStore> _settingsStore = new();
    private readonly Mock<IPythonPathValidator> _pythonPathValidator = new();
    private readonly Mock<IPathValidator> _pathValidator = new();
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _service = new SettingsService(_settingsStore.Object, _pythonPathValidator.Object, _pathValidator.Object);
    }

    private static AppSettings ValidSettings() => new()
    {
        OutputDirectory = @"C:\Output",
        LogDirectory = @"C:\Logs",
        PythonExecutablePath = @"C:\Engine\engine.exe",
        ChunkSizeTokens = 512,
        ChunkOverlapTokens = 50,
        MaxParallelism = 4,
        MaxBatchFiles = 500,
        MaxBatchSizeBytes = 1_000_000
    };

    [Fact]
    public async Task UpdateSettingsAsync_AllFieldsValid_SavesAndReturnsSuccess()
    {
        _pathValidator.Setup(v => v.IsValidDirectoryPath(It.IsAny<string>())).Returns(true);
        _pythonPathValidator
            .Setup(v => v.IsValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.UpdateSettingsAsync(ValidSettings(), CancellationToken.None);

        Assert.True(result.Success);
        _settingsStore.Verify(
            s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSettingsAsync_InvalidOutputDirectory_FailsWithoutSaving()
    {
        _pathValidator.Setup(v => v.IsValidDirectoryPath(@"C:\Output")).Returns(false);

        var result = await _service.UpdateSettingsAsync(ValidSettings(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        _settingsStore.Verify(
            s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_InvalidPythonPath_FailsWithoutSaving()
    {
        _pathValidator.Setup(v => v.IsValidDirectoryPath(It.IsAny<string>())).Returns(true);
        _pythonPathValidator
            .Setup(v => v.IsValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.UpdateSettingsAsync(ValidSettings(), CancellationToken.None);

        Assert.False(result.Success);
        _settingsStore.Verify(
            s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0, 0)]      // chunk size must be positive
    [InlineData(100, 100)]  // overlap must be smaller than chunk size
    [InlineData(100, 150)]  // overlap larger than chunk size
    public async Task UpdateSettingsAsync_InvalidChunkConfiguration_FailsWithoutSaving(
        int chunkSize,
        int overlap)
    {
        _pathValidator.Setup(v => v.IsValidDirectoryPath(It.IsAny<string>())).Returns(true);
        _pythonPathValidator
            .Setup(v => v.IsValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var baseline = ValidSettings();
        var settings = new AppSettings
        {
            OutputDirectory = baseline.OutputDirectory,
            LogDirectory = baseline.LogDirectory,
            PythonExecutablePath = baseline.PythonExecutablePath,
            ChunkSizeTokens = chunkSize,
            ChunkOverlapTokens = overlap,
            MaxParallelism = baseline.MaxParallelism,
            MaxBatchFiles = baseline.MaxBatchFiles,
            MaxBatchSizeBytes = baseline.MaxBatchSizeBytes
        };

        var result = await _service.UpdateSettingsAsync(settings, CancellationToken.None);

        Assert.False(result.Success);
        _settingsStore.Verify(
            s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_NonPositiveMaxParallelism_FailsWithoutSaving()
    {
        _pathValidator.Setup(v => v.IsValidDirectoryPath(It.IsAny<string>())).Returns(true);
        _pythonPathValidator
            .Setup(v => v.IsValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = ValidSettings();
        var invalid = new AppSettings
        {
            OutputDirectory = settings.OutputDirectory,
            LogDirectory = settings.LogDirectory,
            PythonExecutablePath = settings.PythonExecutablePath,
            ChunkSizeTokens = settings.ChunkSizeTokens,
            ChunkOverlapTokens = settings.ChunkOverlapTokens,
            MaxParallelism = 0,
            MaxBatchFiles = settings.MaxBatchFiles,
            MaxBatchSizeBytes = settings.MaxBatchSizeBytes
        };

        var result = await _service.UpdateSettingsAsync(invalid, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetSettingsAsync_DelegatesToStore()
    {
        var expected = ValidSettings();
        _settingsStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await _service.GetSettingsAsync(CancellationToken.None);

        Assert.Same(expected, result);
    }
}
