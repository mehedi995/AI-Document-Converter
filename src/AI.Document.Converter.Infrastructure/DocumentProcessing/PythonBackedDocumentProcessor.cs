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

    // Whether this format offers more than one extraction mode. Declared here
    // rather than inferred from the engine's behaviour, because the engine
    // silently ignores a mode it does not recognise for a format - so without
    // this, asking a PDF for a summary would return a faithful extraction and
    // call it a summary.
    protected virtual bool SupportsSummaryMode => false;

    public Task<DocumentModel> ExtractAsync(string filePath, CancellationToken cancellationToken) =>
        ExtractAsync(filePath, ExtractionMode.Faithful, cancellationToken);

    public async Task<DocumentModel> ExtractAsync(
        string filePath, ExtractionMode mode, CancellationToken cancellationToken)
    {
        if (mode == ExtractionMode.Summary && !SupportsSummaryMode)
        {
            throw new NotSupportedException(
                $"Summary extraction is not available for {Format}. Only faithful extraction is "
                + "defined for this format.");
        }

        var request = new PythonEngineRequest
        {
            Operation = "extract",
            Payload = new ExtractRequestPayload { FilePath = filePath, Format = Format, Mode = mode }
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
