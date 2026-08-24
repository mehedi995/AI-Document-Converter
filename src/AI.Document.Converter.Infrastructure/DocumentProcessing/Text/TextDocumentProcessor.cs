using System.Text;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;

namespace AI.Document.Converter.Infrastructure.DocumentProcessing.Text;

// FR-010/FR-040: the only processor that never touches the Python engine
// (ADR-002) - a TXT file is read directly.
public sealed class TextDocumentProcessor : IDocumentProcessor
{
    public bool CanProcess(string filePath) =>
        SupportedFileTypeExtensions.TryFromExtension(Path.GetExtension(filePath), out var fileType)
        && fileType == SupportedFileType.Txt;

    public async Task<DocumentModel> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            throw new DocumentConversionException(
                $"'{Path.GetFileName(filePath)}' was not found.", ErrorCategory.FileNotFound, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new DocumentConversionException(
                $"Permission denied reading '{Path.GetFileName(filePath)}'.", ErrorCategory.PermissionDenied, ex);
        }
        catch (IOException ex)
        {
            throw new DocumentConversionException(
                $"'{Path.GetFileName(filePath)}' is in use by another process.", ErrorCategory.FileLocked, ex);
        }

        string text;
        try
        {
            var encoding = DetectEncoding(bytes);
            var preambleLength = encoding.GetPreamble().Length;
            text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException ex)
        {
            throw new DocumentConversionException(
                $"'{Path.GetFileName(filePath)}' could not be decoded as text.",
                ErrorCategory.CorruptedDocument,
                ex);
        }

        var fileInfo = new FileInfo(filePath);

        return new DocumentModel
        {
            Metadata = new DocumentMetadata
            {
                SourceFilePath = filePath,
                FileType = SupportedFileType.Txt,
                CreatedDate = fileInfo.CreationTimeUtc,
                ConvertedDate = DateTime.UtcNow
            },
            Sections =
            [
                new Section { Blocks = [new ParagraphBlock { Text = text }] }
            ]
        };
    }

    // FR-040: detect a BOM where present, otherwise assume UTF-8; any encoding
    // used here throws on an invalid byte sequence rather than silently
    // substituting replacement characters, so undecodable content becomes a
    // CorruptedDocument error instead of silently mangled text.
    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(true, true);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return new UTF32Encoding(false, true, true);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return new UTF32Encoding(true, true, true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new UnicodeEncoding(false, true, true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new UnicodeEncoding(true, true, true);
        }

        return new UTF8Encoding(false, true);
    }
}
