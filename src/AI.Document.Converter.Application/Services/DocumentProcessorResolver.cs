using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using System.IO;
using System.Linq;

namespace AI.Document.Converter.Application.Services;

public sealed class DocumentProcessorResolver : IDocumentProcessorResolver
{
    private readonly IReadOnlyList<IDocumentProcessor> _processors;

    public DocumentProcessorResolver(IEnumerable<IDocumentProcessor> processors)
    {
        _processors = processors.ToList();
    }

    public IDocumentProcessor Resolve(string filePath)
    {
        var processor = _processors.FirstOrDefault(p => p.CanProcess(filePath));

        return processor ?? throw new DocumentConversionException(
            $"No document processor is registered for '{Path.GetFileName(filePath)}'.",
            ErrorCategory.UnsupportedFile);
    }
}
