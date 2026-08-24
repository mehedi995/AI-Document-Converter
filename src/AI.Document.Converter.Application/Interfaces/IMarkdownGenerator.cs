using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Interfaces;

// FR-012/FR-013: pure transformation, no I/O - the caller decides where (or
// whether) the result is written to disk (Phase 10, FR-027).
public interface IMarkdownGenerator
{
    string Generate(DocumentModel document);
}
