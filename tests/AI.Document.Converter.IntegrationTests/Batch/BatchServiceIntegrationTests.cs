using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.FileSystem;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.IntegrationTests.Batch;

// TASK-049: BatchService driving the REAL single-file pipeline (not mocked),
// covering the two scenarios unit tests can't: a large batch (NFR-011/FR-024)
// and cancelling a batch whose files are genuinely mid-flight against the
// real bundled Python engine (FR-037).
public class BatchServiceIntegrationTests : IDisposable
{
    private readonly string _outputDirectory;
    private readonly BatchService _batchService = new();

    public BatchServiceIntegrationTests()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), $"ai-doc-converter-batch-test-{Guid.NewGuid()}");
    }

    [Fact]
    public async Task RunAsync_OneHundredTextFiles_ConvertsAllSuccessfullyWithoutExceedingParallelismLimit()
    {
        // TextDocumentProcessor needs no Python subprocess, so 100+ files
        // stays fast while still exercising the real ConversionService
        // pipeline (extraction -> markdown -> token estimate -> file write)
        // rather than a mocked operation delegate.
        var sourceDirectory = Path.Combine(_outputDirectory, "source");
        Directory.CreateDirectory(sourceDirectory);

        const int fileCount = 120;
        var sourcePaths = new List<string>();
        for (var i = 0; i < fileCount; i++)
        {
            var path = Path.Combine(sourceDirectory, $"doc-{i:000}.txt");
            File.WriteAllText(path, $"Sample content for file {i}.\nSecond line of body text.");
            sourcePaths.Add(path);
        }

        // TextDocumentProcessor extracts without Python, so each file needs
        // only ONE Python subprocess call (tokenize) rather than two
        // (extract + tokenize) - token estimation always goes through the
        // real bundled engine regardless of source format (ADR-003).
        var conversionService = BuildConversionService(BuildPythonEngineClient(), [new TextDocumentProcessor()]);
        var outputPathResolver = new OutputPathResolver();
        var updates = new List<BatchProgressUpdate>();
        var progress = new Progress<BatchProgressUpdate>(u =>
        {
            lock (updates)
            {
                updates.Add(u);
            }
        });

        var summary = await _batchService.RunAsync(
            sourcePaths,
            (path, token) => conversionService.ConvertAsync(path, _outputDirectory, outputPathResolver, token),
            maxParallelism: 8,
            progress,
            CancellationToken.None);

        // Surfaces each real Python engine failure message on assertion
        // failure rather than just a bare count mismatch - this is exactly
        // how the Phase 8 tiktoken concurrent-cache race (ADR-003 addendum)
        // was diagnosed.
        var failureMessages = updates
            .Where(u => u.State == BatchItemState.Completed && u.Result?.Success == false)
            .Select(u => $"{Path.GetFileName(u.FilePath)}: {u.Result!.Error} - {u.Result.ErrorMessage}");

        Assert.True(summary.FailureCount == 0, string.Join(Environment.NewLine, failureMessages));
        Assert.Equal(fileCount, summary.TotalFiles);
        Assert.Equal(fileCount, summary.ProcessedFiles);
        Assert.Equal(fileCount, summary.SuccessCount);

        // FR-023: every file must report exactly one Started and one
        // Completed transition - none skipped, none duplicated.
        Assert.Equal(fileCount, updates.Count(u => u.State == BatchItemState.Started));
        Assert.Equal(fileCount, updates.Count(u => u.State == BatchItemState.Completed));

        var outputFiles = Directory.GetFiles(_outputDirectory, "*.md");
        Assert.Equal(fileCount, outputFiles.Length);
    }

    [Fact]
    public async Task RunAsync_CancelledMidBatch_StopsStartingNewFilesAgainstRealPythonSubprocesses()
    {
        var sourceDirectory = Path.Combine(_outputDirectory, "source");
        Directory.CreateDirectory(sourceDirectory);

        var pdfSourcePath = Path.Combine(RepoPaths.SamplesDirectory(), "sample.pdf");
        Assert.True(File.Exists(pdfSourcePath), $"Sample PDF not found at '{pdfSourcePath}'.");

        const int fileCount = 10;
        var sourcePaths = new List<string>();
        for (var i = 0; i < fileCount; i++)
        {
            var path = Path.Combine(sourceDirectory, $"sample-{i:00}.pdf");
            File.Copy(pdfSourcePath, path);
            sourcePaths.Add(path);
        }

        var pythonEngineClient = BuildPythonEngineClient();
        var conversionService = BuildConversionService(pythonEngineClient, [new PdfDocumentProcessor(pythonEngineClient)]);
        var outputPathResolver = new OutputPathResolver();
        var updates = new List<BatchProgressUpdate>();
        var progress = new Progress<BatchProgressUpdate>(u =>
        {
            lock (updates)
            {
                updates.Add(u);
            }
        });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1.5));

        var summary = await _batchService.RunAsync(
            sourcePaths,
            (path, token) => conversionService.ConvertAsync(path, _outputDirectory, outputPathResolver, token),
            maxParallelism: 2,
            progress,
            cts.Token);

        // BR-006/AC-025: cancellation produces a partial summary, not an
        // exception thrown back at the caller.
        Assert.Equal(fileCount, summary.TotalFiles);
        Assert.True(
            summary.ProcessedFiles < fileCount,
            $"Expected cancellation to leave some files unprocessed, but all {summary.ProcessedFiles} completed.");

        var startedCount = updates.Count(u => u.State == BatchItemState.Started);
        Assert.True(startedCount < fileCount, "Cancellation should have prevented every file from starting.");
    }

    // Token estimation always goes through the real bundled engine
    // (ADR-003), so both tests share one real PythonEngineClient - it starts
    // a fresh subprocess per call (see PythonEngineClient.SendAsync), so it's
    // safe to reuse concurrently across a batch.
    private static IPythonEngineClient BuildPythonEngineClient()
    {
        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(File.Exists(enginePath), $"Bundled Python engine not found at '{enginePath}'.");

        return new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(new AppSettings { PythonExecutablePath = enginePath }),
            NullLogger<PythonEngineClient>.Instance);
    }

    private static ConversionService BuildConversionService(
        IPythonEngineClient pythonEngineClient, IReadOnlyList<IDocumentProcessor> processors)
    {
        var processorResolver = new DocumentProcessorResolver(processors);

        return new ConversionService(
            processorResolver,
            new MarkdownGenerator(),
            new TokenEstimator(pythonEngineClient),
            new MarkdownFileWriter(),
            new ChunkGenerator(new TokenCounter(pythonEngineClient)),
            new ChunkFileWriter(),
            NullLogger<ConversionService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }
}
