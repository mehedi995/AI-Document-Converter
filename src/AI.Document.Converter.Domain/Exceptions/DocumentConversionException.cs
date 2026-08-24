using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Domain.Exceptions;

// The one exception type used across the pipeline (docs/11-CODING-STANDARDS.md
// Section 8) - categorized via ErrorCategory (FR-029) rather than one subtype per
// category, to avoid an unnecessary class explosion for nine categories.
public sealed class DocumentConversionException : Exception
{
    public ErrorCategory Category { get; }

    public DocumentConversionException(string message, ErrorCategory category)
        : base(message)
    {
        Category = category;
    }

    public DocumentConversionException(string message, ErrorCategory category, Exception innerException)
        : base(message, innerException)
    {
        Category = category;
    }
}
