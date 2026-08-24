namespace AI.Document.Converter.Domain.ValueObjects;

// Backs the page/slide/sheet reference preservation required by FR-012.
public sealed class SourceLocation
{
    public int? PageNumber { get; init; }
    public int? SlideNumber { get; init; }
    public string? SheetName { get; init; }
}
