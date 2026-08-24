using AI.Document.Converter.Application.Interfaces;

namespace AI.Document.Converter.Infrastructure.FileSystem;

// SEC-002: rejects an empty/relative/malformed path, and rejects a path whose
// normalized form differs from the literal input - the simplest reliable way to
// catch a traversal sequence (e.g. "C:\foo\..\bar") without a denylist of specific
// sequences to filter.
public sealed class PathValidator : IPathValidator
{
    public bool IsValidDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            var normalized = Path.GetFullPath(path);
            return string.Equals(
                TrimTrailingSeparator(normalized),
                TrimTrailingSeparator(path),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string TrimTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
