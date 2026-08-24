using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Interfaces;

// ADR-002: one implementation per format; the Application layer picks the first
// processor whose CanProcess returns true (IDocumentProcessorResolver) rather
// than a factory keyed by format, since five processors don't justify one.
public interface IDocumentProcessor
{
    bool CanProcess(string filePath);

    Task<DocumentModel> ExtractAsync(string filePath, CancellationToken cancellationToken);
}
