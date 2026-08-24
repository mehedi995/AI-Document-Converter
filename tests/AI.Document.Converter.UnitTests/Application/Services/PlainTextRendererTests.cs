using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

public class PlainTextRendererTests
{
    private static DocumentModel DocumentWith(params Section[] sections) => new()
    {
        Metadata = new DocumentMetadata
        {
            SourceFilePath = "sample.pdf",
            FileType = SupportedFileType.Pdf,
            CreatedDate = DateTime.UtcNow,
            ConvertedDate = DateTime.UtcNow
        },
        Sections = [.. sections]
    };

    [Fact]
    public void Render_Heading_ContainsHeadingTextWithoutHashMarks()
    {
        var document = DocumentWith(new Section
        {
            Heading = "Title",
            Blocks = [new ParagraphBlock { Text = "Body" }]
        });

        var text = PlainTextRenderer.Render(document);

        Assert.Contains("Title", text);
        Assert.DoesNotContain("#", text);
    }

    [Fact]
    public void Render_Table_ContainsCellValuesWithoutPipeSyntax()
    {
        var document = DocumentWith(new Section
        {
            Blocks =
            [
                new TableBlock
                {
                    Headers = ["Item", "Value"],
                    Rows = [["Alpha", "100"]]
                }
            ]
        });

        var text = PlainTextRenderer.Render(document);

        Assert.Contains("Item", text);
        Assert.Contains("Alpha", text);
        Assert.Contains("100", text);
        Assert.DoesNotContain("|", text);
        Assert.DoesNotContain("---", text);
    }

    [Fact]
    public void Render_List_ContainsItemsWithoutDashMarkers()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new ListBlock { IsOrdered = false, Items = ["First", "Second"] }]
        });

        var text = PlainTextRenderer.Render(document);

        Assert.Contains("First", text);
        Assert.Contains("Second", text);
        Assert.DoesNotContain("- First", text);
    }

    [Fact]
    public void Render_Link_ContainsTextAndUrlWithoutMarkdownSyntax()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new LinkBlock { Text = "Example", Url = "https://example.com" }]
        });

        var text = PlainTextRenderer.Render(document);

        Assert.Contains("Example", text);
        Assert.Contains("https://example.com", text);
        Assert.DoesNotContain("[Example]", text);
    }

    [Fact]
    public void Render_ImagePlaceholder_ContributesNoText()
    {
        var document = DocumentWith(new Section { Blocks = [new ImagePlaceholderBlock()] });

        var text = PlainTextRenderer.Render(document);

        Assert.Equal("", text.Trim());
    }
}
