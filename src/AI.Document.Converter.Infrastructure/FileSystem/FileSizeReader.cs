using AI.Document.Converter.Application.Interfaces;

namespace AI.Document.Converter.Infrastructure.FileSystem;

public sealed class FileSizeReader : IFileSizeReader
{
    public long GetFileSizeBytes(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing/unreadable file is reported later as a categorized
            // conversion error (Phase 9, FR-029) - Phase 2 import validation
            // simply treats its size as unknown (0) rather than blocking import.
            return 0;
        }
    }
}
