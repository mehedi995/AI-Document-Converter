namespace AI.Document.Converter.IntegrationTests.TestSupport;

internal static class RepoPaths
{
    public static string BundledPythonEnginePath() => Path.Combine(
        RepoRoot(),
        "src",
        "AI.Document.Converter.Python",
        "dist",
        "AIDocumentConverter.PythonEngine",
        "AIDocumentConverter.PythonEngine.exe");

    public static string SamplesDirectory() => Path.Combine(RepoRoot(), "samples");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AI.Document.Converter.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (AI.Document.Converter.sln).");
        }

        return directory.FullName;
    }
}
