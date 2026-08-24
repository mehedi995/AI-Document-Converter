using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

public class FrontMatterBuilderTests
{
    private static DocumentMetadata BaseMetadata(
        int? pageCount = null,
        int? slideCount = null,
        int? sheetCount = null,
        string? author = null) => new()
    {
        SourceFilePath = @"C:\docs\sample.pdf",
        FileType = SupportedFileType.Pdf,
        CreatedDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        ConvertedDate = new DateTime(2026, 1, 2, 6, 7, 8, DateTimeKind.Utc),
        PageCount = pageCount,
        SlideCount = slideCount,
        SheetCount = sheetCount,
        Author = author
    };

    [Fact]
    public void Build_AlwaysIncludesTheFourRequiredFields()
    {
        var result = FrontMatterBuilder.Build(BaseMetadata());

        Assert.StartsWith("---", result);
        Assert.EndsWith("---", result);
        Assert.Contains("source: \"sample.pdf\"", result);
        Assert.Contains("file_type: pdf", result);
        Assert.Contains("created_date: 2026-01-02T03:04:05Z", result);
        Assert.Contains("converted_date: 2026-01-02T06:07:08Z", result);
    }

    [Fact]
    public void Build_UsesFileNameOnly_NotFullSourcePath()
    {
        var result = FrontMatterBuilder.Build(BaseMetadata());

        Assert.DoesNotContain(@"C:\docs", result);
    }

    [Fact]
    public void Build_AuthorAbsent_OmitsFieldEntirely()
    {
        var result = FrontMatterBuilder.Build(BaseMetadata());

        Assert.DoesNotContain("author:", result);
    }

    [Fact]
    public void Build_AuthorPresent_IncludesField()
    {
        var result = FrontMatterBuilder.Build(BaseMetadata(author: "Jane Doe"));

        Assert.Contains("author: \"Jane Doe\"", result);
    }

    [Theory]
    [InlineData(3, null, null, "pages: 3", "slides:", "sheets:")]
    [InlineData(null, 5, null, "slides: 5", "pages:", "sheets:")]
    [InlineData(null, null, 2, "sheets: 2", "pages:", "slides:")]
    public void Build_OnlyThePresentCountField_IsIncluded(
        int? pages,
        int? slides,
        int? sheets,
        string expectedIncluded,
        string omittedA,
        string omittedB)
    {
        var result = FrontMatterBuilder.Build(BaseMetadata(pages, slides, sheets));

        Assert.Contains(expectedIncluded, result);
        Assert.DoesNotContain(omittedA, result);
        Assert.DoesNotContain(omittedB, result);
    }

    [Fact]
    public void Build_AllCountsAbsent_OmitsAllThreeFields()
    {
        var result = FrontMatterBuilder.Build(BaseMetadata());

        Assert.DoesNotContain("pages:", result);
        Assert.DoesNotContain("slides:", result);
        Assert.DoesNotContain("sheets:", result);
    }

    [Fact]
    public void Build_AuthorContainingQuote_IsEscaped()
    {
        var result = FrontMatterBuilder.Build(BaseMetadata(author: "Jane \"JD\" Doe"));

        Assert.Contains("author: \"Jane \\\"JD\\\" Doe\"", result);
    }
}
