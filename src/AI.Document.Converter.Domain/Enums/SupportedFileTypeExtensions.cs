namespace AI.Document.Converter.Domain.Enums;

// FR-005: the single place that decides whether a file extension is supported -
// used by import validation now and, later, by processor resolution (ADR-002).
public static class SupportedFileTypeExtensions
{
    public static bool TryFromExtension(string? extension, out SupportedFileType fileType)
    {
        switch (extension?.TrimStart('.').ToLowerInvariant())
        {
            case "pdf":
                fileType = SupportedFileType.Pdf;
                return true;
            case "docx":
                fileType = SupportedFileType.Docx;
                return true;
            case "xlsx":
                fileType = SupportedFileType.Xlsx;
                return true;
            case "pptx":
                fileType = SupportedFileType.Pptx;
                return true;
            case "txt":
                fileType = SupportedFileType.Txt;
                return true;
            default:
                fileType = default;
                return false;
        }
    }
}
