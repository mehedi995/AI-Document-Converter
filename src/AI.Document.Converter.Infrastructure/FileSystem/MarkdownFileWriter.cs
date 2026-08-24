using AI.Document.Converter.Application.Interfaces;

namespace AI.Document.Converter.Infrastructure.FileSystem;

public sealed class MarkdownFileWriter : IMarkdownFileWriter
{
    public async Task WriteAsync(string outputPath, string markdown, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // FR-044/BR-002: writes here always overwrite an existing file at the
        // same path (the .NET default) - that is the documented, expected
        // behavior for re-converting the same source file.
        await File.WriteAllTextAsync(outputPath, markdown, cancellationToken);
    }
}
