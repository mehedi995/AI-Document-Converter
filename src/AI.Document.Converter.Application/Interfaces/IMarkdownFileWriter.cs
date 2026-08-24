namespace AI.Document.Converter.Application.Interfaces;

// Isolates the one disk write ConversionService needs, keeping the
// Application layer itself free of direct file I/O (docs/11-CODING-STANDARDS.md
// Section 9's separation of concerns).
public interface IMarkdownFileWriter
{
    Task WriteAsync(string outputPath, string markdown, CancellationToken cancellationToken);
}
