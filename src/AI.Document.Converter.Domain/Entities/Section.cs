using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Domain.Entities;

public sealed class Section
{
    public string? Heading { get; init; }

    public int? HeadingLevel { get; init; }

    public required List<ContentBlock> Blocks { get; init; }

    public SourceLocation? Location { get; init; }
}
