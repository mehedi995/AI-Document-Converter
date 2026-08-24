using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Infrastructure.DocumentProcessing.Docx;

public sealed class DocxDocumentProcessor : PythonBackedDocumentProcessor
{
    public DocxDocumentProcessor(IPythonEngineClient pythonEngineClient) : base(pythonEngineClient)
    {
    }

    protected override SupportedFileType Format => SupportedFileType.Docx;
}
