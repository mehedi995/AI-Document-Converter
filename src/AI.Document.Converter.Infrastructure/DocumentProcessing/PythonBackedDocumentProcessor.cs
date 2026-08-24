using AI.Document.Converter.Application.DTOs;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;

namespace AI.Document.Converter.Infrastructure.DocumentProcessing;

// ADR-002: the common logic every Python-backed processor needs (CanProcess by
// extension, ExtractAsync via the "extract" operation, error-to-exception
// mapping) is implemented exactly once here; each subclass supplies only the
// SupportedFileType it owns. TextDocumentProcessor does not derive from this -
// it never touches the Python engine at all.
public abstract class PythonBackedDocumentProcessor : IDocumentProcessor
{
    private readonly IPythonEngineClient _pythonEngineClient;

    protected PythonBackedDocumentProcessor(IPythonEngineClient pythonEngineClient)
    {
        _pythonEngineClient = pythonEngineClient;
    }

    protected abstract SupportedFileType Format { get; }

    public bool CanProcess(string filePath) =>
        SupportedFileTypeExtensions.TryFromExtension(Path.GetExtension(filePath), out var fileType)
        && fileType == Format;

    public async Task<DocumentModel> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        var request = new PythonEngineRequest
        {
            Operation = "extract",
            Payload = new ExtractRequestPayload { FilePath = filePath, Format = Format }
        };

        var response = await _pythonEngineClient.SendAsync<DocumentModel>(request, cancellationToken);

        if (!response.Success || response.Result is null)
        {
            throw new DocumentConversionException(
                response.ErrorMessage ?? "Extraction failed.",
                response.ErrorCategory ?? ErrorCategory.PythonEngineFailure);
        }

        return response.Result;
    }
}
