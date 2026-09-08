namespace AI.Document.Converter.Domain.Enums;

// Stable, machine-readable identifiers for everything the extractor could not
// fully recover (SaaS audit B-05). These are part of the versioned worker
// contract and appear in the export manifest, so a value must never be renamed
// or reused for a different meaning once released - add a new one instead.
//
// Serialized as a camelCase string (e.g. "sheetTruncated"), not an integer, so
// the Python engine can emit them without knowing C# enum ordinals.
public enum WarningCode
{
    // A sheet exceeded the supported extraction limit and only part of it was
    // returned. This is DATA LOSS and must never be presented as a complete
    // extraction (SR-INT-1).
    SheetTruncated,

    // A page/slide yielded no extractable text - typically a scanned image
    // page with OCR disabled (SR-INT-3).
    NoExtractableText,

    // An embedded image was not extracted; a placeholder marks its position
    // (SR-INT-4).
    ImageOmitted,

    // A formula cell had no value cached by Excel, so the cell is empty. The
    // engine will not compute, guess, or execute anything to fill it (SR-INT-2).
    FormulaValueUnavailable,

    // A table is larger than the configured chunk size and therefore occupies a
    // dedicated chunk. The canonical table is preserved intact; downstream
    // consumers with a hard size limit need to know (SR-INT-7).
    TableExceedsChunkSize
}
