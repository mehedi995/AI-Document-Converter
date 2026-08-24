namespace AI.Document.Converter.Application.Interfaces;

public interface IDocumentProcessorResolver
{
    // Throws DocumentConversionException(ErrorCategory.UnsupportedFile) if no
    // registered processor claims the file - callers should already have
    // filtered unsupported files at import time (FR-005), so reaching this is
    // an unexpected state, not the normal rejection path.
    IDocumentProcessor Resolve(string filePath);
}
