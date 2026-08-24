namespace AI.Document.Converter.Domain.Enums;

// OcrText is reserved for Phase 2 (docs/20-FUTURE-ROADMAP.md) and unused in MVP,
// so adding OCR later does not require a DocumentModel shape change
// (docs/09-DATA-MODEL.md Section 3).
public enum ExtractionMethod
{
    NativeText,
    PlaceholderNoText,
    OcrText
}
