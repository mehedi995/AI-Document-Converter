using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.Interfaces;

// ADR-002: one implementation per format; the Application layer picks the first
// processor whose CanProcess returns true (IDocumentProcessorResolver) rather
// than a factory keyed by format, since five processors don't justify one.
public interface IDocumentProcessor
{
    bool CanProcess(string filePath);

    Task<DocumentModel> ExtractAsync(string filePath, CancellationToken cancellationToken);

    // Most formats have exactly one way to be extracted, so this has a default
    // implementation and only the processors that genuinely offer a choice
    // override it.
    //
    // The default THROWS for a non-faithful request rather than quietly
    // extracting faithfully anyway. Silently doing something other than what
    // was asked is the exact shape of the bug this whole mode concept exists
    // to prevent (SaaS audit C-01): a caller that asked for a summary and got
    // a complete extraction would be merely surprised, but the habit of
    // ignoring the argument is how the reverse happens later.
    Task<DocumentModel> ExtractAsync(
        string filePath, ExtractionMode mode, CancellationToken cancellationToken) =>
        mode == ExtractionMode.Faithful
            ? ExtractAsync(filePath, cancellationToken)
            : throw new NotSupportedException(
                $"{GetType().Name} supports only faithful extraction; {mode} was requested.");
}
