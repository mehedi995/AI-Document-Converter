using AI.Document.Converter.Application.Services;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

public class OutputPathResolverTests
{
    private const string OutputDirectory = @"C:\Output";

    [Fact]
    public void ResolveMarkdownOutputPath_SingleFile_ReturnsExpectedPath()
    {
        var resolver = new OutputPathResolver();

        var path = resolver.ResolveMarkdownOutputPath(@"C:\Source\report.pdf", OutputDirectory);

        Assert.Equal(Path.Combine(OutputDirectory, "report.md"), path);
    }

    [Fact]
    public void ResolveMarkdownOutputPath_SameSourceFileTwice_ReturnsSamePath()
    {
        // FR-044: re-conversion overwrites its own prior output, so this must
        // be deterministic across repeated calls for the same source file.
        var resolver = new OutputPathResolver();

        var first = resolver.ResolveMarkdownOutputPath(@"C:\Source\report.pdf", OutputDirectory);
        var second = resolver.ResolveMarkdownOutputPath(@"C:\Source\report.pdf", OutputDirectory);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ResolveMarkdownOutputPath_DifferentSourceFilesSameBaseName_GetsSuffixed()
    {
        // FR-036: two different source files that would otherwise collide.
        var resolver = new OutputPathResolver();

        var first = resolver.ResolveMarkdownOutputPath(@"C:\FolderA\report.pdf", OutputDirectory);
        var second = resolver.ResolveMarkdownOutputPath(@"C:\FolderB\report.pdf", OutputDirectory);

        Assert.NotEqual(first, second);
        Assert.Equal(Path.Combine(OutputDirectory, "report.md"), first);
        Assert.Equal(Path.Combine(OutputDirectory, "report (2).md"), second);
    }

    [Fact]
    public void ResolveMarkdownOutputPath_ThreeCollidingFiles_IncrementsSuffixEachTime()
    {
        var resolver = new OutputPathResolver();

        var first = resolver.ResolveMarkdownOutputPath(@"C:\A\report.pdf", OutputDirectory);
        var second = resolver.ResolveMarkdownOutputPath(@"C:\B\report.docx", OutputDirectory);
        var third = resolver.ResolveMarkdownOutputPath(@"C:\C\report.txt", OutputDirectory);

        Assert.Equal(Path.Combine(OutputDirectory, "report.md"), first);
        Assert.Equal(Path.Combine(OutputDirectory, "report (2).md"), second);
        Assert.Equal(Path.Combine(OutputDirectory, "report (3).md"), third);
    }

    [Fact]
    public void ResolveMarkdownOutputPath_RevisitingAnEarlierCollidingFile_StillReturnsItsOwnAssignedPath()
    {
        var resolver = new OutputPathResolver();

        var first = resolver.ResolveMarkdownOutputPath(@"C:\A\report.pdf", OutputDirectory);
        var second = resolver.ResolveMarkdownOutputPath(@"C:\B\report.pdf", OutputDirectory);
        var firstAgain = resolver.ResolveMarkdownOutputPath(@"C:\A\report.pdf", OutputDirectory);

        Assert.Equal(first, firstAgain);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ResolveMarkdownOutputPath_UnrelatedFileNames_NoCollisionHandlingNeeded()
    {
        var resolver = new OutputPathResolver();

        var first = resolver.ResolveMarkdownOutputPath(@"C:\A\alpha.pdf", OutputDirectory);
        var second = resolver.ResolveMarkdownOutputPath(@"C:\B\beta.pdf", OutputDirectory);

        Assert.Equal(Path.Combine(OutputDirectory, "alpha.md"), first);
        Assert.Equal(Path.Combine(OutputDirectory, "beta.md"), second);
    }
}
