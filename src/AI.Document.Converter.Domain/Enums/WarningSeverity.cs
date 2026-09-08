namespace AI.Document.Converter.Domain.Enums;

// How much a warning should affect the caller's judgement of the result.
//
// The distinction is deliberately blunt, because it drives a real decision:
// a job carrying any Error-severity warning must complete as
// "completed_with_warnings", never as a plain success (SR-INT-1/SR-INT-3).
public enum WarningSeverity
{
    // Content was fully extracted; the note is informational (e.g. an image
    // placeholder was inserted where an image used to be).
    Info,

    // Something is imperfect but no supported content was lost (e.g. a table
    // is larger than the chunk size).
    Warning,

    // Supported content was NOT recovered. The output is incomplete and saying
    // otherwise would be a false claim of complete extraction.
    Error
}
