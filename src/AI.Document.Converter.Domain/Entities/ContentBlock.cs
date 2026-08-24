using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Domain.Entities;

public abstract class ContentBlock
{
    public abstract ContentBlockType Type { get; }
}

public sealed class ParagraphBlock : ContentBlock
{
    public override ContentBlockType Type => ContentBlockType.Paragraph;

    public required string Text { get; init; }
}

public sealed class ListBlock : ContentBlock
{
    public override ContentBlockType Type => ContentBlockType.List;

    public bool IsOrdered { get; init; }

    public required List<string> Items { get; init; }
}

public sealed class TableBlock : ContentBlock
{
    public override ContentBlockType Type => ContentBlockType.Table;

    public required List<string> Headers { get; init; }

    public required List<List<string>> Rows { get; init; }
}

public sealed class LinkBlock : ContentBlock
{
    public override ContentBlockType Type => ContentBlockType.Link;

    public required string Text { get; init; }

    public required string Url { get; init; }
}

// FR-039: embedded images are not extracted in the MVP; this block is the
// placeholder marker inserted at the image's location instead.
public sealed class ImagePlaceholderBlock : ContentBlock
{
    public override ContentBlockType Type => ContentBlockType.ImagePlaceholder;

    public string? AltText { get; init; }
}

// FR-043: a page/slide with no extractable text (e.g., a scanned PDF page) is
// marked, never silently dropped.
public sealed class UnextractableTextBlock : ContentBlock
{
    public override ContentBlockType Type => ContentBlockType.UnextractableText;

    public required ExtractionMethod Reason { get; init; }

    public required string Note { get; init; }
}
