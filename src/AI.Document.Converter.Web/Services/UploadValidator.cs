using System.IO.Compression;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Web.Services;

public sealed record UploadRejection(string Code, string Message);

public sealed record UploadValidationResult(
    bool IsAccepted,
    SupportedFileType FileType,
    UploadRejection? Rejection)
{
    public static UploadValidationResult Accepted(SupportedFileType fileType) =>
        new(true, fileType, null);

    public static UploadValidationResult Rejected(string code, string message) =>
        new(false, default, new UploadRejection(code, message));
}

// SR-SEC-1 (audit E-02, E-03). The desktop application validated the file
// extension and nothing else, which is adequate for a file the user picked from
// their own disk and completely inadequate for a public upload endpoint.
//
// What this checks, and why each one is here:
//
//   extension          cheap first filter, and it decides which parser runs
//   magic bytes        the extension is attacker-controlled; the content is the
//                      only thing that says what the file actually is
//   declared size      a multipart part can claim one length and send another
//   OOXML container    docx/xlsx/pptx are ZIPs; a ZIP that is not one of those
//                      has no business reaching a parser
//   expansion ratio    decompression bombs: a few KB that inflate to gigabytes
//   entry count        thousands of tiny entries exhaust the parser instead
//
// It does NOT attempt to decide whether a document is malicious in content.
// That is what the sandboxed worker is for; this is about not handing the
// parser something it was never meant to see.
public sealed class UploadValidator
{
    // Generous enough for real business documents, small enough that one upload
    // cannot fill the disk. Server-enforced; a client-supplied size is not a limit.
    public const long MaxFileSizeBytes = 100L * 1024 * 1024;

    // A legitimate office document compresses well but not absurdly. 200:1 is
    // far above anything observed in practice and far below what a bomb needs.
    public const long MaxCompressionRatio = 200;

    public const long MaxUncompressedBytes = 500L * 1024 * 1024;

    public const int MaxArchiveEntries = 5_000;

    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46]; // %PDF
    private static readonly byte[] OleSignature =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    // Which entry inside the ZIP proves what an OOXML file really is. Checking
    // for the ZIP magic alone would let a .zip renamed to .docx through.
    private static readonly Dictionary<SupportedFileType, string> OoxmlMarkerEntry = new()
    {
        [SupportedFileType.Docx] = "word/document.xml",
        [SupportedFileType.Xlsx] = "xl/workbook.xml",
        [SupportedFileType.Pptx] = "ppt/presentation.xml"
    };

    public async Task<UploadValidationResult> ValidateAsync(
        string fileName, long declaredSizeBytes, Stream content, CancellationToken cancellationToken)
    {
        if (!SupportedFileTypeExtensions.TryFromExtension(Path.GetExtension(fileName), out var fileType))
        {
            return UploadValidationResult.Rejected(
                "unsupportedFileType",
                "That file type is not supported. Supported types are PDF, DOCX, XLSX, PPTX and TXT.");
        }

        if (declaredSizeBytes <= 0)
        {
            return UploadValidationResult.Rejected("emptyFile", "The file is empty.");
        }

        if (declaredSizeBytes > MaxFileSizeBytes)
        {
            return UploadValidationResult.Rejected(
                "fileTooLarge",
                $"That file is larger than the {MaxFileSizeBytes / (1024 * 1024)} MB limit.");
        }

        if (!content.CanSeek)
        {
            throw new ArgumentException(
                "Validation needs a seekable stream so content can be inspected and then re-read.",
                nameof(content));
        }

        // The actual byte count, not what the request claimed. A multipart part
        // can declare any length it likes.
        var actualSize = content.Length;
        if (actualSize != declaredSizeBytes)
        {
            return UploadValidationResult.Rejected(
                "sizeMismatch", "The upload was incomplete or altered in transit. Please try again.");
        }

        content.Position = 0;
        var header = new byte[8];
        var headerLength = await content.ReadAtLeastAsync(
            header, header.Length, throwOnEndOfStream: false, cancellationToken);
        content.Position = 0;

        var signatureResult = CheckSignature(fileType, header.AsSpan(0, headerLength));
        if (signatureResult is not null)
        {
            return signatureResult;
        }

        if (OoxmlMarkerEntry.ContainsKey(fileType))
        {
            var archiveResult = InspectOoxmlContainer(fileType, content, actualSize);
            content.Position = 0;
            if (archiveResult is not null)
            {
                return archiveResult;
            }
        }

        return UploadValidationResult.Accepted(fileType);
    }

    private static UploadValidationResult? CheckSignature(SupportedFileType fileType, ReadOnlySpan<byte> header)
    {
        switch (fileType)
        {
            case SupportedFileType.Pdf:
                return header.StartsWith(PdfSignature)
                    ? null
                    : UploadValidationResult.Rejected(
                        "contentTypeMismatch",
                        "That file is named as a PDF but its contents are not a PDF.");

            case SupportedFileType.Docx:
            case SupportedFileType.Xlsx:
            case SupportedFileType.Pptx:
                if (header.StartsWith(OleSignature))
                {
                    // BR-003: an encrypted OOXML file is an OLE wrapper, not a
                    // ZIP. Rejecting it here with a clear reason beats letting
                    // the parser fail later with "corrupted document".
                    return UploadValidationResult.Rejected(
                        "passwordProtected",
                        "That file appears to be password-protected, which is not supported.");
                }

                return header.StartsWith(ZipSignature)
                    ? null
                    : UploadValidationResult.Rejected(
                        "contentTypeMismatch",
                        "That file's contents do not match its file type.");

            case SupportedFileType.Txt:
                // Text has no signature to check. Encoding is validated during
                // extraction, which rejects undecodable bytes rather than
                // silently substituting replacement characters (FR-040).
                return null;

            default:
                return UploadValidationResult.Rejected(
                    "unsupportedFileType", "That file type is not supported.");
        }
    }

    private static UploadValidationResult? InspectOoxmlContainer(
        SupportedFileType fileType, Stream content, long compressedSize)
    {
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

            if (archive.Entries.Count > MaxArchiveEntries)
            {
                return UploadValidationResult.Rejected(
                    "archiveTooManyEntries",
                    "That file contains an unusual number of internal parts and was not processed.");
            }

            // Summed from the entry headers rather than by extracting. Reading
            // the declared sizes is what lets us refuse a bomb WITHOUT first
            // inflating it, which is the whole point.
            long totalUncompressed = 0;
            foreach (var entry in archive.Entries)
            {
                totalUncompressed += entry.Length;

                if (totalUncompressed > MaxUncompressedBytes)
                {
                    return UploadValidationResult.Rejected(
                        "archiveExpansionLimit",
                        "That file expands to an unusually large size and was not processed.");
                }
            }

            if (compressedSize > 0 && totalUncompressed / compressedSize > MaxCompressionRatio)
            {
                return UploadValidationResult.Rejected(
                    "archiveExpansionLimit",
                    "That file expands to an unusually large size and was not processed.");
            }

            var markerEntry = OoxmlMarkerEntry[fileType];
            if (archive.GetEntry(markerEntry) is null)
            {
                return UploadValidationResult.Rejected(
                    "contentTypeMismatch",
                    "That file's contents do not match its file type.");
            }

            return null;
        }
        catch (InvalidDataException)
        {
            return UploadValidationResult.Rejected(
                "corruptedFile", "That file could not be read. It may be damaged.");
        }
    }

    // The stored name. Never used to build a storage key or a path - keys are
    // server-generated - but it is echoed back to the user and put in export
    // manifests, so control characters and directory separators are stripped
    // rather than trusted (SR-SEC-1, SR-SEC-3).
    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);

        var cleaned = new string(name
            .Where(c => !char.IsControl(c) && c != '/' && c != '\\')
            .ToArray())
            .Trim();

        return cleaned.Length == 0
            ? "unnamed"
            : cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }
}
