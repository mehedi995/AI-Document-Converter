using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Interfaces;

// FR-021: writes each chunk as its own numbered file with metadata
// identifying the source document and sequence number.
public interface IChunkFileWriter
{
    Task WriteAsync(string outputDirectory, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);
}
