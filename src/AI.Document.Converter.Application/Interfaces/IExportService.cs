using AI.Document.Converter.Application.Models;

namespace AI.Document.Converter.Application.Interfaces;

// FR-027/028/046: individual Markdown/chunk files are already the per-file
// export (written directly by IMarkdownFileWriter/IChunkFileWriter during
// conversion) - this interface covers the one new Phase 10 capability,
// packaging a whole batch's already-converted output into a single ZIP.
public interface IExportService
{
    Task<ExportResult> ExportBatchAsZipAsync(
        IReadOnlyList<ExportItem> items,
        string zipDestinationPath,
        CancellationToken cancellationToken);
}
