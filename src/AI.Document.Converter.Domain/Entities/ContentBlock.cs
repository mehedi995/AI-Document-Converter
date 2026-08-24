using System.Text.Json.Serialization;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Domain.Entities;

// The JSON discriminator ("type") is System.Text.Json's built-in polymorphism
// support (.NET 7+), not a separate C# property - a parallel discriminator
// property would duplicate exactly what this attribute already encodes and would
// collide with it on the wire once camelCase naming is applied (both would want
// to own the JSON property named "type"). Code that needs to branch on block kind
// uses a C# pattern-match switch on the type itself instead.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ParagraphBlock), "paragraph")]
[JsonDerivedType(typeof(ListBlock), "list")]
[JsonDerivedType(typeof(TableBlock), "table")]
[JsonDerivedType(typeof(LinkBlock), "link")]
[JsonDerivedType(typeof(ImagePlaceholderBlock), "imagePlaceholder")]
[JsonDerivedType(typeof(UnextractableTextBlock), "unextractableText")]
public abstract class ContentBlock
{
}

public sealed class ParagraphBlock : ContentBlock
{
    public required string Text { get; init; }
}

public sealed class ListBlock : ContentBlock
{
    public bool IsOrdered { get; init; }

    public required List<string> Items { get; init; }
}

public sealed class TableBlock : ContentBlock
{
    public required List<string> Headers { get; init; }

    public required List<List<string>> Rows { get; init; }
}

public sealed class LinkBlock : ContentBlock
{
    public required string Text { get; init; }

    public required string Url { get; init; }
}

// FR-039: embedded images are not extracted in the MVP; this block is the
// placeholder marker inserted at the image's location instead.
public sealed class ImagePlaceholderBlock : ContentBlock
{
    public string? AltText { get; init; }
}

// FR-043: a page/slide with no extractable text (e.g., a scanned PDF page) is
// marked, never silently dropped.
public sealed class UnextractableTextBlock : ContentBlock
{
    public required ExtractionMethod Reason { get; init; }

    public required string Note { get; init; }
}
