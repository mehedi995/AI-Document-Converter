using System.Globalization;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Infrastructure.FileSystem;

public sealed class ChunkFileWriter : IChunkFileWriter
{
    public async Task WriteAsync(
        string outputDirectory,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);

        foreach (var chunk in chunks)
        {
            var fileName = $"chunk_{chunk.SequenceNumber.ToString("D3", CultureInfo.InvariantCulture)}.md";
            var path = Path.Combine(outputDirectory, fileName);

            // FR-021: identifies the source document and this chunk's
            // sequence number, mirroring FrontMatterBuilder's front-matter
            // style for the main Markdown output.
            var header =
                $"---\n" +
                $"source: \"{chunk.SourceFileName}\"\n" +
                $"chunk: {chunk.SequenceNumber} of {chunks.Count}\n" +
                $"token_count: {chunk.TokenCount}\n" +
                $"overlap_tokens: {chunk.OverlapTokens}\n" +
                $"---\n\n";

            await File.WriteAllTextAsync(path, header + chunk.Content, cancellationToken);
        }
    }
}
