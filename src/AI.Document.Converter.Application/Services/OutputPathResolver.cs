using AI.Document.Converter.Application.Interfaces;

namespace AI.Document.Converter.Application.Services;

public sealed class OutputPathResolver : IOutputPathResolver
{
    // Maps an assigned output path to whichever source file claimed it, so a
    // second call for the SAME source file gets the SAME path back (FR-044),
    // while a DIFFERENT source file that would collide gets a numeric suffix
    // instead (FR-036). FR-036 also allows mirroring the relative source
    // folder structure, but folder import is top-level only in the MVP
    // (FR-004), so there is no nested structure to mirror - the numeric
    // suffix is the only strategy that actually applies yet.
    private readonly Dictionary<string, string> _assignedPaths = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveMarkdownOutputPath(string sourceFilePath, string outputDirectory)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var candidate = Path.Combine(outputDirectory, baseName + ".md");

        if (IsFreeFor(candidate, sourceFilePath))
        {
            _assignedPaths[candidate] = sourceFilePath;
            return candidate;
        }

        var suffix = 2;
        string suffixed;
        do
        {
            suffixed = Path.Combine(outputDirectory, $"{baseName} ({suffix}).md");
            suffix++;
        } while (!IsFreeFor(suffixed, sourceFilePath));

        _assignedPaths[suffixed] = sourceFilePath;
        return suffixed;
    }

    private bool IsFreeFor(string candidatePath, string sourceFilePath) =>
        !_assignedPaths.TryGetValue(candidatePath, out var claimedBy)
        || string.Equals(claimedBy, sourceFilePath, StringComparison.OrdinalIgnoreCase);
}
