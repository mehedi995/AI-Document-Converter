using AI.Document.Converter.Domain.Enums;
using Xunit;

namespace AI.Document.Converter.UnitTests.Domain.Enums;

public class SupportedFileTypeExtensionsTests
{
    [Theory]
    [InlineData(".pdf", SupportedFileType.Pdf)]
    [InlineData(".docx", SupportedFileType.Docx)]
    [InlineData(".xlsx", SupportedFileType.Xlsx)]
    [InlineData(".pptx", SupportedFileType.Pptx)]
    [InlineData(".txt", SupportedFileType.Txt)]
    [InlineData(".PDF", SupportedFileType.Pdf)]
    [InlineData("pdf", SupportedFileType.Pdf)]
    public void TryFromExtension_SupportedExtension_ReturnsTrueAndCorrectType(
        string extension,
        SupportedFileType expected)
    {
        var result = SupportedFileTypeExtensions.TryFromExtension(extension, out var fileType);

        Assert.True(result);
        Assert.Equal(expected, fileType);
    }

    [Theory]
    [InlineData(".html")]
    [InlineData(".csv")]
    [InlineData("")]
    [InlineData(null)]
    public void TryFromExtension_UnsupportedExtension_ReturnsFalse(string? extension)
    {
        var result = SupportedFileTypeExtensions.TryFromExtension(extension, out _);

        Assert.False(result);
    }
}
