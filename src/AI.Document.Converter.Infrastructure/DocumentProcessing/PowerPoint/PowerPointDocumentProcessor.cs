using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Infrastructure.DocumentProcessing.PowerPoint;

public sealed class PowerPointDocumentProcessor : PythonBackedDocumentProcessor
{
    public PowerPointDocumentProcessor(IPythonEngineClient pythonEngineClient) : base(pythonEngineClient)
    {
    }

    protected override SupportedFileType Format => SupportedFileType.Pptx;
}
