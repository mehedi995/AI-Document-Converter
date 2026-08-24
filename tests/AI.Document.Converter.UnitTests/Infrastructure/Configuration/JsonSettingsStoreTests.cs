using System.Text.Json;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.Configuration;
using AI.Document.Converter.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AI.Document.Converter.UnitTests.Infrastructure.Configuration;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _settingsFilePath;

    public JsonSettingsStoreTests()
    {
        _settingsFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-settings.json");
    }

    [Fact]
    public async Task SaveAsync_ThenReadFileDirectly_PersistsExactValues()
    {
        var store = new JsonSettingsStore(
            _settingsFilePath,
            new StaticOptionsMonitor<AppSettings>(new AppSettings()),
            NullLogger<JsonSettingsStore>.Instance);

        var settings = new AppSettings
        {
            OutputDirectory = @"C:\Output",
            ChunkSizeTokens = 256,
            ChunkOverlapTokens = 25,
            MaxParallelism = 2
        };

        await store.SaveAsync(settings, CancellationToken.None);

        var json = await File.ReadAllTextAsync(_settingsFilePath);
        var persisted = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(persisted);
        Assert.Equal(settings.OutputDirectory, persisted!.OutputDirectory);
        Assert.Equal(settings.ChunkSizeTokens, persisted.ChunkSizeTokens);
        Assert.Equal(settings.ChunkOverlapTokens, persisted.ChunkOverlapTokens);
        Assert.Equal(settings.MaxParallelism, persisted.MaxParallelism);
    }

    [Fact]
    public async Task SaveAsync_DirectoryDoesNotExistYet_CreatesIt()
    {
        var nestedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "settings.json");
        var store = new JsonSettingsStore(
            nestedPath,
            new StaticOptionsMonitor<AppSettings>(new AppSettings()),
            NullLogger<JsonSettingsStore>.Instance);

        await store.SaveAsync(new AppSettings(), CancellationToken.None);

        Assert.True(File.Exists(nestedPath));

        Directory.Delete(Path.GetDirectoryName(nestedPath)!, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ReturnsCurrentOptionsMonitorValue()
    {
        var expected = new AppSettings { OutputDirectory = @"C:\FromOptions" };
        var store = new JsonSettingsStore(
            _settingsFilePath,
            new StaticOptionsMonitor<AppSettings>(expected),
            NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Same(expected, loaded);
    }

    public void Dispose()
    {
        if (File.Exists(_settingsFilePath))
        {
            File.Delete(_settingsFilePath);
        }

        var tempFile = _settingsFilePath + ".tmp";
        if (File.Exists(tempFile))
        {
            File.Delete(tempFile);
        }
    }
}
