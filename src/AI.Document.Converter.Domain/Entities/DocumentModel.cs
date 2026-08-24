namespace AI.Document.Converter.Domain.Entities;

// The normalized document model every IDocumentProcessor implementation maps its
// source format into (FR-011), before Markdown generation.
public sealed class DocumentModel
{
    public required DocumentMetadata Metadata { get; init; }

    public required List<Section> Sections { get; init; }
}
