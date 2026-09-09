using System.IO.Compression;
using System.Text;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Web.Services;

namespace AI.Document.Converter.Web.Tests;

// SR-SEC-1 (audit E-02, E-03). The desktop app checked the file extension and
// nothing else. These tests exist to prove the upload boundary does more than
// that, and to pin the specific attacks it is meant to stop.
public sealed class UploadValidatorTests
{
    private readonly UploadValidator _validator = new();

    private static MemoryStream Bytes(params byte[] content) => new(content);

    private static MemoryStream OoxmlArchive(string markerEntry, string content = "<xml/>")
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(markerEntry);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        buffer.Position = 0;
        return buffer;
    }

    private Task<UploadValidationResult> ValidateAsync(string fileName, MemoryStream content) =>
        _validator.ValidateAsync(fileName, content.Length, content, CancellationToken.None);

    [Fact]
    public async Task RealPdf_IsAccepted()
    {
        using var pdf = Bytes(Encoding.ASCII.GetBytes("%PDF-1.7\n% test\n"));

        var result = await ValidateAsync("report.pdf", pdf);

        Assert.True(result.IsAccepted);
        Assert.Equal(SupportedFileType.Pdf, result.FileType);
    }

    // The core of E-02. An extension check alone accepts this.
    [Fact]
    public async Task ExecutableRenamedToPdf_IsRejected()
    {
        using var notAPdf = Bytes(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00); // MZ header

        var result = await ValidateAsync("invoice.pdf", notAPdf);

        Assert.False(result.IsAccepted);
        Assert.Equal("contentTypeMismatch", result.Rejection!.Code);
    }

    // A plain ZIP renamed to .docx has the right magic bytes but is not a Word
    // document. Checking the ZIP signature alone would let it through to the
    // parser.
    [Fact]
    public async Task PlainZipRenamedToDocx_IsRejected()
    {
        using var zip = OoxmlArchive("some/unrelated/file.txt");

        var result = await ValidateAsync("contract.docx", zip);

        Assert.False(result.IsAccepted);
        Assert.Equal("contentTypeMismatch", result.Rejection!.Code);
    }

    [Theory]
    [InlineData("report.docx", "word/document.xml")]
    [InlineData("budget.xlsx", "xl/workbook.xml")]
    [InlineData("deck.pptx", "ppt/presentation.xml")]
    public async Task GenuineOoxmlContainers_AreAccepted(string fileName, string markerEntry)
    {
        using var archive = OoxmlArchive(markerEntry);

        var result = await ValidateAsync(fileName, archive);

        Assert.True(result.IsAccepted, result.Rejection?.Message);
    }

    // BR-003: an encrypted OOXML file is an OLE wrapper, not a ZIP. Naming the
    // real reason beats letting the parser fail later with "corrupted".
    [Fact]
    public async Task PasswordProtectedOoxml_IsRejectedWithItsOwnReason()
    {
        using var ole = Bytes(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1);

        var result = await ValidateAsync("secret.xlsx", ole);

        Assert.False(result.IsAccepted);
        Assert.Equal("passwordProtected", result.Rejection!.Code);
    }

    // E-03: a decompression bomb. Highly compressible content inside a valid
    // OOXML container - the entry headers reveal the expansion before anything
    // is inflated, which is the point.
    [Fact]
    public async Task DecompressionBomb_IsRejectedWithoutInflatingIt()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(entry.Open());
            // 60 MB of a single repeated character compresses to a few KB.
            writer.Write(new string('A', 60 * 1024 * 1024));
        }

        buffer.Position = 0;

        var result = await _validator.ValidateAsync(
            "bomb.docx", buffer.Length, buffer, CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal("archiveExpansionLimit", result.Rejection!.Code);
    }

    [Fact]
    public async Task ArchiveWithTooManyEntries_IsRejected()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("word/document.xml");
            for (var i = 0; i < UploadValidator.MaxArchiveEntries + 10; i++)
            {
                archive.CreateEntry($"junk/{i}.bin");
            }
        }

        buffer.Position = 0;

        var result = await _validator.ValidateAsync(
            "many.docx", buffer.Length, buffer, CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal("archiveTooManyEntries", result.Rejection!.Code);
    }

    // A multipart part can claim one length and send another. The declared size
    // is never the size that is trusted.
    [Fact]
    public async Task DeclaredSizeThatDisagreesWithActualContent_IsRejected()
    {
        using var pdf = Bytes(Encoding.ASCII.GetBytes("%PDF-1.7\n"));

        var result = await _validator.ValidateAsync(
            "report.pdf", declaredSizeBytes: 5_000_000, pdf, CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal("sizeMismatch", result.Rejection!.Code);
    }

    [Fact]
    public async Task OversizedFile_IsRejectedBeforeAnyContentIsRead()
    {
        using var content = Bytes(Encoding.ASCII.GetBytes("%PDF-1.7\n"));

        var result = await _validator.ValidateAsync(
            "huge.pdf",
            declaredSizeBytes: UploadValidator.MaxFileSizeBytes + 1,
            content,
            CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal("fileTooLarge", result.Rejection!.Code);
    }

    [Fact]
    public async Task EmptyFile_IsRejected()
    {
        using var empty = new MemoryStream();

        var result = await ValidateAsync("empty.pdf", empty);

        Assert.False(result.IsAccepted);
        Assert.Equal("emptyFile", result.Rejection!.Code);
    }

    [Fact]
    public async Task UnsupportedExtension_IsRejected()
    {
        using var content = Bytes(Encoding.ASCII.GetBytes("MZ"));

        var result = await ValidateAsync("payload.exe", content);

        Assert.False(result.IsAccepted);
        Assert.Equal("unsupportedFileType", result.Rejection!.Code);
    }

    // TXT has no signature to check, so anything with a .txt name and real
    // bytes is accepted here; undecodable content is caught during extraction
    // (FR-040), which throws rather than substituting replacement characters.
    [Fact]
    public async Task PlainText_IsAccepted()
    {
        using var text = Bytes(Encoding.UTF8.GetBytes("hello"));

        var result = await ValidateAsync("notes.txt", text);

        Assert.True(result.IsAccepted);
        Assert.Equal(SupportedFileType.Txt, result.FileType);
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"..\..\windows\system32\config", "config")]
    [InlineData("C:\\Users\\bob\\secret.pdf", "secret.pdf")]
    [InlineData("", "unnamed")]
    [InlineData("   ", "unnamed")]
    public void SanitizeFileName_StripsPathsAndNeverReturnsEmpty(string input, string expected)
    {
        Assert.Equal(expected, UploadValidator.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_RemovesControlCharacters()
    {
        var sanitized = UploadValidator.SanitizeFileName("re\u0000po\u001Frt.pdf");

        Assert.Equal("report.pdf", sanitized);
        Assert.DoesNotContain(sanitized, char.IsControl);
    }
}
