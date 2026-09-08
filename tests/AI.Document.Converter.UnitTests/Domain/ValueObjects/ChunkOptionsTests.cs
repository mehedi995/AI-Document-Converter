using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.UnitTests.Domain.ValueObjects;

// SR-INT-6 (SaaS audit C-09). These combinations were previously accepted and
// produced quietly degenerate output rather than an error, which is the hardest
// kind of bug to notice from the result alone.
public class ChunkOptionsTests
{
    [Theory]
    [InlineData(512, 50)]
    [InlineData(512, 0)]
    [InlineData(1, 0)]
    public void TryValidate_WithOverlapSmallerThanSize_Succeeds(int size, int overlap)
    {
        var options = new ChunkOptions { ChunkSizeTokens = size, OverlapTokens = overlap };

        Assert.True(options.TryValidate(out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryValidate_WithNonPositiveSize_Fails(int size)
    {
        var options = new ChunkOptions { ChunkSizeTokens = size, OverlapTokens = 0 };

        Assert.False(options.TryValidate(out var error));
        Assert.Contains("greater than zero", error);
    }

    [Fact]
    public void TryValidate_WithNegativeOverlap_Fails()
    {
        var options = new ChunkOptions { ChunkSizeTokens = 512, OverlapTokens = -1 };

        Assert.False(options.TryValidate(out var error));
        Assert.Contains("negative", error);
    }

    // The genuinely dangerous case: the units carried over as overlap already
    // fill the next chunk, so every following unit immediately overflows and
    // the output degenerates into near-duplicate chunks.
    [Theory]
    [InlineData(100, 100)]
    [InlineData(100, 150)]
    public void TryValidate_WithOverlapAtOrAboveSize_Fails(int size, int overlap)
    {
        var options = new ChunkOptions { ChunkSizeTokens = size, OverlapTokens = overlap };

        Assert.False(options.TryValidate(out var error));
        Assert.Contains("smaller than the chunk size", error);
    }
}
