using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Persistence.Presets;

public sealed record ConversionPreset(
    string Name,
    string DisplayName,
    string Description,
    bool GenerateChunks,
    int ChunkSizeTokens,
    int ChunkOverlapTokens)
{
    public ChunkOptions ToChunkOptions() => new()
    {
        ChunkSizeTokens = ChunkSizeTokens,
        OverlapTokens = ChunkOverlapTokens
    };
}

// SaaS §10: "a small number of understandable presets", with advanced chunk
// options behind progressive disclosure.
//
// Server-controlled and looked up BY NAME. The upload form submits a preset
// name, never chunk numbers - a client-supplied chunk size would let anyone
// request a one-token chunk size and turn a single document into hundreds of
// thousands of chunks. An unknown name falls back to the default rather than
// failing, because a stale bookmark should not break an upload.
//
// Deliberately a static table rather than a database row: presets change with
// a deployment, not at runtime, and the values are validated at startup. It
// moves into versioned plan configuration when billing arrives (SR-BIL-6).
public static class ConversionPresets
{
    public const string DefaultName = "default";

    private static readonly Dictionary<string, ConversionPreset> ByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultName] = new(
                DefaultName,
                "Standard",
                "Faithful extraction with RAG-ready chunks. Suitable for most documents.",
                GenerateChunks: true,
                // 512/50 are the values fixed in the SRS (FR-018) and carried
                // over from the desktop defaults.
                ChunkSizeTokens: 512,
                ChunkOverlapTokens: 50),

            ["markdown-only"] = new(
                "markdown-only",
                "Markdown only",
                "Converted Markdown without chunk files. Choose this if you are not building a "
                + "retrieval index.",
                GenerateChunks: false,
                ChunkSizeTokens: 512,
                ChunkOverlapTokens: 50),

            ["large-chunks"] = new(
                "large-chunks",
                "Large chunks",
                "Fewer, larger chunks. Useful for models with a large context window.",
                GenerateChunks: true,
                ChunkSizeTokens: 1024,
                ChunkOverlapTokens: 100)
        };

    public static IReadOnlyCollection<ConversionPreset> All => ByName.Values;

    // Never throws. An unrecognised name means a stale form or an edited
    // request, and neither should cost the customer their upload - they get
    // the default, which is a documented, safe configuration.
    public static ConversionPreset Resolve(string? name) =>
        name is not null && ByName.TryGetValue(name, out var preset)
            ? preset
            : ByName[DefaultName];

    public static bool IsKnown(string? name) =>
        name is not null && ByName.ContainsKey(name);
}
