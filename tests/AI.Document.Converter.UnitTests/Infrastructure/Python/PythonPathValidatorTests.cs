using AI.Document.Converter.Infrastructure.Python;
using Xunit;

namespace AI.Document.Converter.UnitTests.Infrastructure.Python;

public class PythonPathValidatorTests : IDisposable
{
    private readonly PythonPathValidator _validator = new();
    private readonly string _tempExePath;
    private readonly string _tempNonExePath;

    public PythonPathValidatorTests()
    {
        _tempExePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.exe");
        File.WriteAllText(_tempExePath, "not a real executable, existence is all that's checked here");

        _tempNonExePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        File.WriteAllText(_tempNonExePath, "not an executable");
    }

    [Fact]
    public async Task IsValidAsync_ExistingExeFile_ReturnsTrue()
    {
        var result = await _validator.IsValidAsync(_tempExePath, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsValidAsync_NonExistentPath_ReturnsFalse()
    {
        var result = await _validator.IsValidAsync(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.exe"),
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsValidAsync_NonExeExtension_ReturnsFalse()
    {
        var result = await _validator.IsValidAsync(_tempNonExePath, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsValidAsync_EmptyPath_ReturnsFalse()
    {
        var result = await _validator.IsValidAsync(string.Empty, CancellationToken.None);

        Assert.False(result);
    }

    public void Dispose()
    {
        File.Delete(_tempExePath);
        File.Delete(_tempNonExePath);
    }
}
