using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Interfaces;

// FR-001-005, NFR-013. Single/multiple/drag-drop/folder import all reduce to "a
// flat list of candidate file paths" by the time they reach this interface -
// folder expansion happens in the caller (Presentation), not here.
public interface IImportService
{
    ImportResult Import(
        IReadOnlyList<string> filePaths,
        int currentBatchFileCount,
        long currentBatchSizeBytes,
        AppSettings settings);
}
