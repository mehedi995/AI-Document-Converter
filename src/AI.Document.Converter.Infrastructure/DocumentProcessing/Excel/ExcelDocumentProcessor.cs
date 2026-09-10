using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Infrastructure.DocumentProcessing.Excel;

public sealed class ExcelDocumentProcessor : PythonBackedDocumentProcessor
{
    public ExcelDocumentProcessor(IPythonEngineClient pythonEngineClient) : base(pythonEngineClient)
    {
    }

    protected override SupportedFileType Format => SupportedFileType.Xlsx;

    // The one format with a real choice (SR-INT-1). A spreadsheet can be far
    // larger than anything worth reading in full, so a header-plus-sample
    // preview is genuinely useful - as long as it announces itself, which the
    // engine's Error-severity sheetTruncated warning makes it do.
    protected override bool SupportsSummaryMode => true;
}
