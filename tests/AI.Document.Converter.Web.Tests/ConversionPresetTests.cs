using AI.Document.Converter.Persistence.Presets;

namespace AI.Document.Converter.Web.Tests;

// SaaS §10 and SR-SEC-2. Presets are the reason chunk sizes never come from the
// client: the form submits a name, the server decides the numbers.
public sealed class ConversionPresetTests
{
    [Fact]
    public void EveryPresetProducesValidChunkOptions()
    {
        // ChunkOptions.TryValidate rejects size <= 0 and overlap >= size, both
        // of which produce degenerate output (audit C-09). A preset that
        // shipped with an invalid combination would be a config-time bug that
        // only appeared at conversion time.
        Assert.All(ConversionPresets.All, preset =>
        {
            var valid = preset.ToChunkOptions().TryValidate(out var error);
            Assert.True(valid, $"Preset '{preset.Name}' has invalid chunk options: {error}");
        });
    }

    [Fact]
    public void UnknownPresetFallsBackToDefaultRatherThanFailing()
    {
        // A stale bookmark or an edited form should not cost the customer their
        // upload; they get a documented, safe configuration instead.
        Assert.Equal(ConversionPresets.DefaultName, ConversionPresets.Resolve("no-such-preset").Name);
        Assert.Equal(ConversionPresets.DefaultName, ConversionPresets.Resolve(null).Name);
        Assert.Equal(ConversionPresets.DefaultName, ConversionPresets.Resolve(string.Empty).Name);
    }

    [Fact]
    public void PresetNamesAreCaseInsensitive()
    {
        Assert.Equal(ConversionPresets.DefaultName, ConversionPresets.Resolve("DEFAULT").Name);
        Assert.True(ConversionPresets.IsKnown("Markdown-Only"));
    }

    [Fact]
    public void MarkdownOnlyPresetDisablesChunking()
    {
        Assert.False(ConversionPresets.Resolve("markdown-only").GenerateChunks);
        Assert.True(ConversionPresets.Resolve(ConversionPresets.DefaultName).GenerateChunks);
    }

    // FR-018 fixes these in the SRS, and the desktop uses the same numbers.
    // Pinned so a future edit is a deliberate decision rather than a drift.
    [Fact]
    public void DefaultPresetMatchesTheDocumentedChunkSettings()
    {
        var preset = ConversionPresets.Resolve(ConversionPresets.DefaultName);

        Assert.Equal(512, preset.ChunkSizeTokens);
        Assert.Equal(50, preset.ChunkOverlapTokens);
    }

    [Fact]
    public void EveryPresetHasCustomerFacingText()
    {
        Assert.All(ConversionPresets.All, preset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(preset.Description));
        });
    }
}
