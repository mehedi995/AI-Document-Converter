namespace AI.Document.Converter.Domain.ValueObjects;

// Persisted as JSON (docs/09-DATA-MODEL.md Section 5); there is no database for MVP.
// Field defaults here match the values fixed in docs/03-SRS.md (FR-018, NFR-013).
// Environment-tailored defaults (e.g., MaxParallelism from processor count) are
// computed by Infrastructure when a settings file is first created, not here.
public sealed class AppSettings
{
    public string OutputDirectory { get; init; } = string.Empty;

    public int ChunkSizeTokens { get; init; } = 512;

    public int ChunkOverlapTokens { get; init; } = 50;

    public string PythonExecutablePath { get; init; } = string.Empty;

    public string LogDirectory { get; init; } = string.Empty;

    // docs/07-TECHNICAL-ARCHITECTURE.md Section 3: protects against a hung Python
    // subprocess; a timed-out file is reported as ConversionFailure (FR-029) and
    // can be retried (FR-030).
    public int PythonEngineTimeoutSeconds { get; init; } = 120;

    public int MaxParallelism { get; init; } = 4;

    public int MaxBatchFiles { get; init; } = 500;

    public long MaxBatchSizeBytes { get; init; } = 5_368_709_120; // 5 GB

    public List<string> TokenizerDisplayProviders { get; init; } = ["ClaudeStyle", "Gpt4oStyle"];
}
