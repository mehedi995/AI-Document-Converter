namespace AI.Document.Converter.Domain.Enums;

// SR-INT-1. How completely a document should be extracted.
//
// This exists because the two answers are not interchangeable and the
// difference must be a deliberate, visible choice. The desktop app's original
// behaviour was to sample large spreadsheets at 200 rows and report plain
// success - a 1,000-row sheet came back as 5 rows with no warning of any kind
// (SaaS audit C-01). Faithful is the default precisely so that silent loss
// cannot be the thing that happens when nobody chose.
public enum ExtractionMode
{
    // Everything, or a clear failure. A document larger than the engine can
    // complete is refused rather than quietly reduced.
    Faithful = 0,

    // A header-plus-sample preview. Genuinely useful for looking at a huge
    // spreadsheet quickly, and it must never be presented as complete: the
    // engine raises an Error-severity sheetTruncated warning, which forces the
    // job to "completed with warnings".
    Summary = 1
}
