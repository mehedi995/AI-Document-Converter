namespace AI.Document.Converter.Application.Interfaces;

// FR-036/FR-044: pure path-naming logic, no disk I/O (actually writing a file
// is Phase 10's ExportService). Stateful across calls - one instance must be
// used for an entire batch so collisions between different source files in
// that batch are actually caught; a fresh instance per batch is the caller's
// responsibility (there is deliberately no long-lived DI registration for
// this - see docs/15-IMPLEMENTATION-PLAN.md Section 4).
public interface IOutputPathResolver
{
    // Returns the same path every time for the same sourceFilePath (FR-044:
    // re-conversion overwrites its own prior output). A different
    // sourceFilePath that would otherwise produce the same output name gets a
    // numeric suffix instead (FR-036).
    string ResolveMarkdownOutputPath(string sourceFilePath, string outputDirectory);
}
