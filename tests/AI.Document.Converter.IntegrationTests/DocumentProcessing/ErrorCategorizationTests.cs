using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Docx;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Excel;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.DocumentProcessing.PowerPoint;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.FileSystem;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.DocumentProcessing;

// Phase 9 (TASK-055): each FR-029 error category, driven against the real
// bundled Python engine and genuine failure conditions (a real OS-level file
// lock, real garbage bytes, a real OLE-compound-file header) rather than a
// mocked response - the categorization logic itself lives on the Python side
// (extractors/common.py) and is only meaningfully verified this way.
public class ErrorCategorizationTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly IPythonEngineClient _pythonEngineClient;

    public ErrorCategorizationTests()
    {
        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(File.Exists(enginePath), $"Bundled Python engine not found at '{enginePath}'.");

        _pythonEngineClient = new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(new AppSettings { PythonExecutablePath = enginePath }),
            NullLogger<PythonEngineClient>.Instance);

        _workingDirectory = Path.Combine(Path.GetTempPath(), $"ai-doc-converter-error-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_workingDirectory);
    }

    // An OLE Compound File header - what a password-protected OOXML file
    // (docx/xlsx/pptx) actually is on disk, per common.py's
    // check_not_encrypted_ooxml. Fabricating just the header is enough: the
    // check never reads past it.
    private static readonly byte[] OleCompoundFileHeader =
        [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1, .. new byte[50]];

    [Theory]
    [InlineData("locked.pdf")]
    [InlineData("locked.docx")]
    [InlineData("locked.txt")]
    public async Task Extract_FileLockedByAnotherHandle_ThrowsFileLockedCategory(string fileName)
    {
        // TextDocumentProcessor's own FileShare/IOException handling is a
        // separate .NET-only code path from the four Python-backed
        // extractors' check_file_accessible - both need this scenario.
        var samplePath = Path.Combine(RepoPaths.SamplesDirectory(), "sample" + Path.GetExtension(fileName));
        var lockedPath = Path.Combine(_workingDirectory, fileName);
        File.Copy(samplePath, lockedPath);

        var processor = BuildProcessor(fileName);

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var exception = await Assert.ThrowsAsync<DocumentConversionException>(
                () => processor.ExtractAsync(lockedPath, CancellationToken.None));

            Assert.Equal(ErrorCategory.FileLocked, exception.Category);
        }
    }

    [Fact]
    public async Task Extract_PathIsADirectoryNotAFile_ThrowsPermissionDeniedCategory()
    {
        // Windows returns ERROR_ACCESS_DENIED (5) for CreateFileW against a
        // directory without FILE_FLAG_BACKUP_SEMANTICS - a safe, deterministic
        // way to reach the real permissionDenied path without touching any
        // ACLs (verified directly against extractors/common.py before relying
        // on it here, same as the fileLocked winerror investigation).
        var directoryPath = Path.Combine(_workingDirectory, "not-actually-a-file.docx");
        Directory.CreateDirectory(directoryPath);

        var processor = new DocxDocumentProcessor(_pythonEngineClient);

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => processor.ExtractAsync(directoryPath, CancellationToken.None));

        Assert.Equal(ErrorCategory.PermissionDenied, exception.Category);
    }

    [Theory]
    [InlineData("garbage.pdf")]
    [InlineData("garbage.docx")]
    [InlineData("garbage.xlsx")]
    [InlineData("garbage.pptx")]
    public async Task Extract_GarbageBytes_ThrowsCorruptedDocumentCategory(string fileName)
    {
        var path = Path.Combine(_workingDirectory, fileName);
        File.WriteAllBytes(path, "not a real document, just garbage bytes"u8.ToArray());

        var processor = BuildProcessor(fileName);

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => processor.ExtractAsync(path, CancellationToken.None));

        Assert.Equal(ErrorCategory.CorruptedDocument, exception.Category);
    }

    [Theory]
    [InlineData("encrypted.docx")]
    [InlineData("encrypted.xlsx")]
    [InlineData("encrypted.pptx")]
    public async Task Extract_PasswordProtectedOoxml_ThrowsUnsupportedFileCategory_NotCorruptedDocument(string fileName)
    {
        // BR-003: must be reported as "not supported", never a generic
        // corrupted-document failure - this is the exact scenario Phase 9
        // closed, since python-docx/openpyxl/python-pptx can't open an
        // encrypted package at all and would otherwise surface it as
        // corruptedDocument.
        var path = Path.Combine(_workingDirectory, fileName);
        File.WriteAllBytes(path, OleCompoundFileHeader);

        var processor = BuildProcessor(fileName);

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => processor.ExtractAsync(path, CancellationToken.None));

        Assert.Equal(ErrorCategory.UnsupportedFile, exception.Category);
    }

    [Fact]
    public async Task Extract_PasswordProtectedPdf_ThrowsUnsupportedFileCategory_NotCorruptedDocument()
    {
        // docs/17-UNIT-TEST-PLAN.md's PDF Extraction plan requires this
        // scenario explicitly - a real encrypted PDF (samples/password-
        // protected-sample.pdf, generated by scripts/generate-samples.py),
        // not a fabricated header, since pymupdf's own needs_pass check
        // (unlike the OOXML formats above) only ever sees a real encrypted
        // file, never a bare signature.
        var path = Path.Combine(RepoPaths.SamplesDirectory(), "password-protected-sample.pdf");
        Assert.True(File.Exists(path), $"Sample not found at '{path}'. Run scripts/generate-samples.py.");

        var processor = new PdfDocumentProcessor(_pythonEngineClient);

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => processor.ExtractAsync(path, CancellationToken.None));

        Assert.Equal(ErrorCategory.UnsupportedFile, exception.Category);
    }

    [Fact]
    public async Task Extract_TxtWithUndecodableByteSequence_ThrowsCorruptedDocumentCategory()
    {
        // FR-040: an overlong encoding is a byte sequence no BOM check
        // matches and strict UTF-8 universally rejects - TextDocumentProcessor
        // throws on invalid bytes rather than silently substituting
        // replacement characters (see DetectEncoding's throwOnInvalidBytes).
        var path = Path.Combine(_workingDirectory, "undecodable.txt");
        File.WriteAllBytes(path, [0xC0, 0x80]);

        var processor = new TextDocumentProcessor();

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => processor.ExtractAsync(path, CancellationToken.None));

        Assert.Equal(ErrorCategory.CorruptedDocument, exception.Category);
    }

    [Fact]
    public async Task ConvertAsync_RetryAfterLockReleased_SucceedsOnSecondAttempt()
    {
        // AC-020: "a previously failed file whose underlying issue has been
        // resolved" reattempts and succeeds - verified at the mechanism
        // Retry actually calls (ConversionService.ConvertAsync again for the
        // one file), consistent with this project's WPF ViewModels not
        // having a unit-test harness (docs/16-TEST-STRATEGY.md).
        var sourcePath = Path.Combine(_workingDirectory, "retry-me.docx");
        File.Copy(Path.Combine(RepoPaths.SamplesDirectory(), "sample.docx"), sourcePath);

        var service = BuildConversionService();
        var outputPathResolver = new OutputPathResolver();

        ConversionResult failure;
        using (new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            failure = await service.ConvertAsync(sourcePath, _workingDirectory, outputPathResolver, CancellationToken.None);
        }

        Assert.False(failure.Success);
        Assert.Equal(ErrorCategory.FileLocked, failure.Error);

        var retry = await service.ConvertAsync(sourcePath, _workingDirectory, outputPathResolver, CancellationToken.None);

        Assert.True(retry.Success, retry.ErrorMessage);
        Assert.True(File.Exists(retry.OutputPath));
    }

    private IDocumentProcessor BuildProcessor(string fileName) => Path.GetExtension(fileName) switch
    {
        ".pdf" => new PdfDocumentProcessor(_pythonEngineClient),
        ".docx" => new DocxDocumentProcessor(_pythonEngineClient),
        ".xlsx" => new ExcelDocumentProcessor(_pythonEngineClient),
        ".pptx" => new PowerPointDocumentProcessor(_pythonEngineClient),
        ".txt" => new TextDocumentProcessor(),
        var ext => throw new NotSupportedException($"No processor mapped for extension '{ext}' in this test.")
    };

    private ConversionService BuildConversionService()
    {
        var processorResolver = new DocumentProcessorResolver(
        [
            new TextDocumentProcessor(),
            new DocxDocumentProcessor(_pythonEngineClient)
        ]);

        return new ConversionService(
            processorResolver,
            new MarkdownGenerator(),
            new TokenEstimator(_pythonEngineClient),
            new MarkdownFileWriter(),
            new ChunkGenerator(new TokenCounter(_pythonEngineClient)),
            new ChunkFileWriter(),
            NullLogger<ConversionService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }
}
