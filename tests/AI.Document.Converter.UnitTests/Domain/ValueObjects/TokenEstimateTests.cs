using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.UnitTests.Domain.ValueObjects;

// FR-015 and SR-INT-5 (SaaS audit C-11/C-12). The honesty rules for the one
// number a customer is most likely to quote back at us.
public class TokenEstimateTests
{
    private static TokenEstimate Estimate(int original, int converted) => new()
    {
        OriginalGpt4oStyle = original,
        ConvertedGpt4oStyle = converted,
        OriginalClaudeStyle = original,
        ConvertedClaudeStyle = converted
    };

    [Fact]
    public void ReductionPercent_WithRealReduction_IsPositive()
    {
        Assert.Equal(75d, Estimate(1000, 250).ReductionPercentGpt4oStyle);
    }

    // FR-015: Markdown syntax can legitimately cost MORE tokens than the plain
    // text baseline. That must surface as a negative number, not be clamped to
    // zero - a guaranteed saving is exactly the claim we must never make.
    [Fact]
    public void ReductionPercent_WhenOutputIsLarger_IsNegative()
    {
        var reduction = Estimate(100, 150).ReductionPercentGpt4oStyle;

        Assert.NotNull(reduction);
        Assert.Equal(-50d, reduction!.Value);
    }

    // SR-INT-5: there is no percentage of nothing. Returning 0 here would
    // assert a specific, false measurement ("0% reduction") where the only
    // honest answer is "not applicable".
    [Fact]
    public void ReductionPercent_WithEmptyBaseline_IsNullNotZero()
    {
        Assert.Null(Estimate(0, 0).ReductionPercentGpt4oStyle);
        Assert.Null(Estimate(0, 40).ReductionPercentGpt4oStyle);
    }

    [Fact]
    public void ReductionPercent_WithNoChange_IsZero()
    {
        Assert.Equal(0d, Estimate(500, 500).ReductionPercentGpt4oStyle);
    }
}
