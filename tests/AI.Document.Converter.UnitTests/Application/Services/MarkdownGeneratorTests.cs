using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

public class MarkdownGeneratorTests
{
    private readonly MarkdownGenerator _generator = new();

    private static DocumentMetadata Metadata() => new()
    {
        SourceFilePath = "sample.pdf",
        FileType = SupportedFileType.Pdf,
        CreatedDate = DateTime.UtcNow,
        ConvertedDate = DateTime.UtcNow
    };

    private static DocumentModel DocumentWith(params Section[] sections) => new()
    {
        Metadata = Metadata(),
        Sections = [.. sections]
    };

    [Fact]
    public void Generate_StartsWithFrontMatter()
    {
        var document = DocumentWith(new Section { Blocks = [new ParagraphBlock { Text = "Body" }] });

        var markdown = _generator.Generate(document);

        Assert.StartsWith("---", markdown);
    }

    [Fact]
    public void Generate_Heading_RendersAtCorrectLevel()
    {
        var document = DocumentWith(new Section
        {
            Heading = "Title",
            HeadingLevel = 2,
            Blocks = [new ParagraphBlock { Text = "Body" }]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("## Title", markdown);
    }

    [Fact]
    public void Generate_NoHeading_OmitsHeadingLine()
    {
        var document = DocumentWith(new Section { Blocks = [new ParagraphBlock { Text = "Body" }] });

        var markdown = _generator.Generate(document);

        Assert.DoesNotContain("#", markdown.Replace("---", ""));
    }

    [Fact]
    public void Generate_UnorderedList_RendersWithDashMarkers()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new ListBlock { IsOrdered = false, Items = ["First", "Second"] }]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("- First", markdown);
        Assert.Contains("- Second", markdown);
    }

    [Fact]
    public void Generate_OrderedList_RendersWithSequentialNumbers()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new ListBlock { IsOrdered = true, Items = ["First", "Second", "Third"] }]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("1. First", markdown);
        Assert.Contains("2. Second", markdown);
        Assert.Contains("3. Third", markdown);
    }

    [Fact]
    public void Generate_Table_RendersHeaderSeparatorAndRows()
    {
        var document = DocumentWith(new Section
        {
            Blocks =
            [
                new TableBlock
                {
                    Headers = ["Item", "Value"],
                    Rows = [["Alpha", "100"], ["Beta", "200"]]
                }
            ]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("| Item | Value |", markdown);
        Assert.Contains("| --- | --- |", markdown);
        Assert.Contains("| Alpha | 100 |", markdown);
        Assert.Contains("| Beta | 200 |", markdown);
    }

    [Fact]
    public void Generate_TableCellContainingPipe_IsEscaped()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new TableBlock { Headers = ["A"], Rows = [["x|y"]] }]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains(@"x\|y", markdown);
    }

    [Fact]
    public void Generate_LinkBlock_RendersMarkdownLinkSyntax()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new LinkBlock { Text = "Example", Url = "https://example.com" }]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("[Example](https://example.com)", markdown);
    }

    [Fact]
    public void Generate_ImagePlaceholderWithoutAltText_RendersGenericPlaceholder()
    {
        var document = DocumentWith(new Section { Blocks = [new ImagePlaceholderBlock()] });

        var markdown = _generator.Generate(document);

        Assert.Contains("*[Image omitted]*", markdown);
    }

    [Fact]
    public void Generate_ImagePlaceholderWithAltText_IncludesAltText()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new ImagePlaceholderBlock { AltText = "A chart" }]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("*[Image: A chart]*", markdown);
    }

    [Fact]
    public void Generate_UnextractableTextBlock_RendersAsBlockquote()
    {
        var document = DocumentWith(new Section
        {
            Blocks =
            [
                new UnextractableTextBlock
                {
                    Reason = ExtractionMethod.PlaceholderNoText,
                    Note = "Page 3 has no extractable text."
                }
            ]
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("> Page 3 has no extractable text.", markdown);
    }

    [Fact]
    public void Generate_SectionWithPageReference_RendersPageLine()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new ParagraphBlock { Text = "Body" }],
            Location = new SourceLocation { PageNumber = 2 }
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("*Page 2*", markdown);
    }

    [Fact]
    public void Generate_SectionWithSlideReference_RendersSlideLine()
    {
        var document = DocumentWith(new Section
        {
            Blocks = [new ParagraphBlock { Text = "Body" }],
            Location = new SourceLocation { SlideNumber = 4 }
        });

        var markdown = _generator.Generate(document);

        Assert.Contains("*Slide 4*", markdown);
    }

    [Fact]
    public void Generate_SectionWithSheetNameOnly_DoesNotRenderAReferenceLine()
    {
        // The xlsx extractor already uses the sheet name as the section
        // heading, so a separate reference line would be redundant.
        var document = DocumentWith(new Section
        {
            Heading = "Data",
            Blocks = [new ParagraphBlock { Text = "Body" }],
            Location = new SourceLocation { SheetName = "Data" }
        });

        var markdown = _generator.Generate(document);

        Assert.DoesNotContain("*Sheet", markdown);
    }

    [Fact]
    public void Generate_MultipleSections_PreservesOrder()
    {
        var document = DocumentWith(
            new Section { Heading = "First", Blocks = [new ParagraphBlock { Text = "One" }] },
            new Section { Heading = "Second", Blocks = [new ParagraphBlock { Text = "Two" }] });

        var markdown = _generator.Generate(document);

        Assert.True(markdown.IndexOf("First", StringComparison.Ordinal) < markdown.IndexOf("Second", StringComparison.Ordinal));
    }
}
