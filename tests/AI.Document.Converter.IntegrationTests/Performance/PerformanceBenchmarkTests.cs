using System.Diagnostics;
using System.Text;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.FileSystem;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.IntegrationTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AI.Document.Converter.IntegrationTests.Performance;

// TASK-061: measures the docs/03-SRS.md Section 8 benchmark matrix for real,
// against the real bundled Python engine - "not yet measured" until this ran.
// Tagged Performance and excluded from the routine `dotnet test` run per
// docs/16-TEST-STRATEGY.md ("run manually per release, Sprint 11/Phase 11"):
//
//   dotnet test --filter Category=Performance   (this suite only)
//   dotnet test --filter Category!=Performance   (routine run, excludes this)
//
// The 100 MB single-file case can still intermittently fail fast when run
// chained with the other five cases here (Large Object Heap fragmentation
// across several large sequential allocations in one process - see R-20,
// docs/18-RISK-ASSESSMENT.md, and the measured-results note in
// docs/03-SRS.md Section 8). It is completely reliable run alone:
//   dotnet test --filter FullyQualifiedName~ConvertAsync_SingleFileAtTargetSize
public class PerformanceBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _workingDirectory;
    private readonly ConversionService _conversionService;
    private readonly BatchService _batchService = new();

    public PerformanceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;

        var enginePath = RepoPaths.BundledPythonEnginePath();
        Assert.True(File.Exists(enginePath), $"Bundled Python engine not found at '{enginePath}'.");

        // A generous timeout - this is measuring real elapsed time for large
        // inputs, not verifying the default 120s production timeout.
        var pythonEngineClient = new PythonEngineClient(
            new StaticOptionsMonitor<AppSettings>(
                new AppSettings { PythonExecutablePath = enginePath, PythonEngineTimeoutSeconds = 600 }),
            NullLogger<PythonEngineClient>.Instance);

        var processorResolver = new DocumentProcessorResolver([new TextDocumentProcessor()]);

        _conversionService = new ConversionService(
            processorResolver,
            new MarkdownGenerator(),
            new TokenEstimator(pythonEngineClient),
            new MarkdownFileWriter(),
            new ChunkGenerator(new TokenCounter(pythonEngineClient)),
            new ChunkFileWriter(),
            NullLogger<ConversionService>.Instance);

        _workingDirectory = Path.Combine(Path.GetTempPath(), $"ai-doc-converter-perf-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_workingDirectory);
    }

    // Repeated real English-like sentences, not random bytes - representative
    // of what tiktoken actually has to tokenize, unlike an incompressible
    // random blob a real document would never contain.
    private static void WriteTextFileOfSize(string path, long targetSizeBytes)
    {
        const string sentence =
            "The quarterly report highlights steady growth across all regional offices. ";
        var sentenceBytes = Encoding.UTF8.GetByteCount(sentence);

        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        var written = 0L;
        while (written < targetSizeBytes)
        {
            writer.Write(sentence);
            written += sentenceBytes;
        }
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData("1 MB", 1L * 1024 * 1024, 5)]
    [InlineData("10 MB", 10L * 1024 * 1024, 20)]
    [InlineData("50 MB", 50L * 1024 * 1024, 90)]
    [InlineData("100 MB", 100L * 1024 * 1024, 180)]
    public async Task ConvertAsync_SingleFileAtTargetSize_CompletesWithinBenchmark(
        string profileLabel, long sizeBytes, int targetSeconds)
    {
        // This class runs several 100MB+/100-file scenarios back-to-back in
        // one process (xUnit does not isolate [Theory] cases into separate
        // processes) - without this, a large case can inherit memory
        // pressure left over from whichever heavy case ran immediately
        // before it, timing/resource behavior a real single-session desktop
        // app use would never actually see.
        ReleaseMemoryPressure();

        var path = Path.Combine(_workingDirectory, $"perf-{sizeBytes}.txt");
        WriteTextFileOfSize(path, sizeBytes);

        var stopwatch = Stopwatch.StartNew();
        var result = await _conversionService.ConvertAsync(
            path, _workingDirectory, new OutputPathResolver(), CancellationToken.None);
        stopwatch.Stop();

        _output.WriteLine($"[{profileLabel}] elapsed: {stopwatch.Elapsed.TotalSeconds:0.0}s (target: <= {targetSeconds}s)");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(
            stopwatch.Elapsed.TotalSeconds <= targetSeconds,
            $"[{profileLabel}] took {stopwatch.Elapsed.TotalSeconds:0.0}s, exceeding the {targetSeconds}s target.");
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(10, 60)]
    [InlineData(100, 8 * 60)]
    public async Task BatchService_RunAsync_BatchOfFilesAtAverageSize_CompletesWithinBenchmark(
        int fileCount, int targetSeconds)
    {
        ReleaseMemoryPressure();

        const long averageFileSizeBytes = 2L * 1024 * 1024;
        var sourceDirectory = Path.Combine(_workingDirectory, $"batch-{fileCount}");
        Directory.CreateDirectory(sourceDirectory);

        var paths = new List<string>();
        for (var i = 0; i < fileCount; i++)
        {
            var path = Path.Combine(sourceDirectory, $"doc-{i:0000}.txt");
            WriteTextFileOfSize(path, averageFileSizeBytes);
            paths.Add(path);
        }

        var outputPathResolver = new OutputPathResolver();
        var stopwatch = Stopwatch.StartNew();

        var summary = await _batchService.RunAsync(
            paths,
            (filePath, ct) => _conversionService.ConvertAsync(filePath, _workingDirectory, outputPathResolver, ct),
            maxParallelism: 4,
            new Progress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        stopwatch.Stop();

        _output.WriteLine(
            $"[Batch of {fileCount}] elapsed: {stopwatch.Elapsed.TotalSeconds:0.0}s " +
            $"(target: <= {targetSeconds}s), succeeded {summary.SuccessCount}/{summary.TotalFiles}");

        Assert.Equal(fileCount, summary.SuccessCount);
        Assert.True(
            stopwatch.Elapsed.TotalSeconds <= targetSeconds,
            $"[Batch of {fileCount}] took {stopwatch.Elapsed.TotalSeconds:0.0}s, exceeding the {targetSeconds}s target.");
    }

    private static void ReleaseMemoryPressure()
    {
        // Plain GC.Collect() does NOT compact the Large Object Heap by
        // default - repeated large (>85KB) string/byte-array allocations
        // from differently-sized prior cases in this same process fragment
        // the LOH, and a later even-larger allocation can then fail to find
        // a contiguous block even though total free memory looks sufficient
        // (confirmed as the actual cause here: isolating this class's cases
        // still failed on the largest one specifically, fast, until this
        // was added). CompactOnce must be requested before the collection
        // that should perform it.
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }
}
