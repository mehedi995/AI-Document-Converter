using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;

public sealed class PdfDocumentProcessor : PythonBackedDocumentProcessor
{
    public PdfDocumentProcessor(IPythonEngineClient pythonEngineClient) : base(pythonEngineClient)
    {
    }

    protected override SupportedFileType Format => SupportedFileType.Pdf;
}
