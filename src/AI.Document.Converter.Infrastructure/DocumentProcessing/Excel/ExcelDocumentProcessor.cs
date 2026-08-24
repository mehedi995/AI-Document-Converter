using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Infrastructure.DocumentProcessing.Excel;

public sealed class ExcelDocumentProcessor : PythonBackedDocumentProcessor
{
    public ExcelDocumentProcessor(IPythonEngineClient pythonEngineClient) : base(pythonEngineClient)
    {
    }

    protected override SupportedFileType Format => SupportedFileType.Xlsx;
}
