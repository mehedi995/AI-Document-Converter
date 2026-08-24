using AI.Document.Converter.Infrastructure.FileSystem;
using Xunit;

namespace AI.Document.Converter.UnitTests.Infrastructure.FileSystem;

public class PathValidatorTests
{
    private readonly PathValidator _validator = new();

    [Fact]
    public void IsValidDirectoryPath_RootedAbsolutePath_ReturnsTrue()
    {
        var result = _validator.IsValidDirectoryPath(@"C:\Users\Someone\Documents\Output");

        Assert.True(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidDirectoryPath_EmptyOrWhitespace_ReturnsFalse(string? path)
    {
        var result = _validator.IsValidDirectoryPath(path!);

        Assert.False(result);
    }

    [Fact]
    public void IsValidDirectoryPath_RelativePath_ReturnsFalse()
    {
        var result = _validator.IsValidDirectoryPath(@"Output\Folder");

        Assert.False(result);
    }

    [Fact]
    public void IsValidDirectoryPath_TraversalSequence_ReturnsFalse()
    {
        // Normalizes to "C:\Bar", which differs from the literal input - SEC-002.
        var result = _validator.IsValidDirectoryPath(@"C:\Foo\..\Bar");

        Assert.False(result);
    }

    [Fact]
    public void IsValidDirectoryPath_InvalidPathCharacter_ReturnsFalse()
    {
        var result = _validator.IsValidDirectoryPath("C:\\Output\\" + '\0' + "Folder");

        Assert.False(result);
    }
}
