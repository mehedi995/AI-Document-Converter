namespace AI.Document.Converter.Domain.ValueObjects;

// Carries both tokenizer profiles per ADR-003. The UI is responsible for labeling
// each clearly as an estimate (FR-017) - this type does not enforce that itself.
public sealed class TokenEstimate
{
    public required int OriginalClaudeStyle { get; init; }

    public required int ConvertedClaudeStyle { get; init; }

    public required int OriginalGpt4oStyle { get; init; }

    public required int ConvertedGpt4oStyle { get; init; }

    // May be zero or negative (FR-015) - reduction is not guaranteed for every
    // document, and callers must not assume a positive value.
    //
    // NULL when the baseline is zero (SR-INT-5, audit C-11). There is no
    // percentage of nothing, and the previous behavior of returning 0 was worse
    // than a crash would have been: it silently asserted "0% reduction", a
    // specific and false measurement, where the honest answer is "not
    // applicable". Callers must render null as N/A, never coalesce it to 0.
    public double? ReductionPercentGpt4oStyle =>
        OriginalGpt4oStyle == 0
            ? null
            : (OriginalGpt4oStyle - ConvertedGpt4oStyle) / (double)OriginalGpt4oStyle * 100;
}
