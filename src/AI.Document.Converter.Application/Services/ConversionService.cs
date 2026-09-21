using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using AI.Document.Converter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AI.Document.Converter.Application.Services;

public sealed class ConversionService : IConversionService
{
    private readonly IDocumentProcessorResolver _processorResolver;
    private readonly IMarkdownGenerator _markdownGenerator;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IMarkdownFileWriter _markdownFileWriter;
    private readonly IChunkGenerator _chunkGenerator;
    private readonly IChunkFileWriter _chunkFileWriter;
    private readonly ILogger<ConversionService> _logger;

    public ConversionService(
        IDocumentProcessorResolver processorResolver,
        IMarkdownGenerator markdownGenerator,
        ITokenEstimator tokenEstimator,
        IMarkdownFileWriter markdownFileWriter,
        IChunkGenerator chunkGenerator,
        IChunkFileWriter chunkFileWriter,
        ILogger<ConversionService> logger)
    {
        _processorResolver = processorResolver;
        _markdownGenerator = markdownGenerator;
        _tokenEstimator = tokenEstimator;
        _markdownFileWriter = markdownFileWriter;
        _chunkGenerator = chunkGenerator;
        _chunkFileWriter = chunkFileWriter;
        _logger = logger;
    }

    public Task<ConversionResult> ConvertAsync(
        string filePath,
        string outputDirectory,
        IOutputPathResolver outputPathResolver,
        CancellationToken cancellationToken) =>
        ConvertAsync(filePath, outputDirectory, outputPathResolver, null, cancellationToken);

    public Task<ConversionResult> ConvertAsync(
        string filePath,
        string outputDirectory,
        IOutputPathResolver outputPathResolver,
        ChunkOptions? chunkOptions,
        CancellationToken cancellationToken) =>
        ExecuteAsync("Conversion", filePath, async () =>
        {
            // The single extraction both outputs are built from (audit D-05).
            var document = await _processorResolver.Resolve(filePath).ExtractAsync(filePath, cancellationToken);

            var markdown = _markdownGenerator.Generate(document);
            var plainText = PlainTextRenderer.Render(document);
            var tokens = await _tokenEstimator.EstimateAsync(plainText, markdown, cancellationToken);

            var outputPath = outputPathResolver.ResolveMarkdownOutputPath(filePath, outputDirectory);
            await _markdownFileWriter.WriteAsync(outputPath, markdown, cancellationToken);

            if (chunkOptions is null)
            {
                return new ConversionResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    Tokens = tokens,
                    CompletedStages = PipelineStage.Converted,
                    Metadata = document.Metadata,
                    Warnings = document.Warnings
                };
            }

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var chunkResult = await _chunkGenerator.GenerateChunksAsync(
                document, chunkOptions, $"{baseName}.md", cancellationToken);

            var chunksDirectory = Path.Combine(outputDirectory, "chunks", baseName);
            await _chunkFileWriter.WriteAsync(chunksDirectory, chunkResult.Chunks, cancellationToken);

            return new ConversionResult
            {
                Success = true,
                // The Markdown, not the chunk directory: it is the primary
                // output, and the file list's "open output" action points here.
                OutputPath = outputPath,
                Tokens = tokens,
                ChunkCount = chunkResult.Chunks.Count,
                // Both stages really did complete, and the batch summary reads
                // this to decide what to report.
                CompletedStages = PipelineStage.Converted | PipelineStage.Chunked,
                Metadata = document.Metadata,
                // Extraction warnings AND chunking warnings, same as the
                // standalone chunk path - an oversized table is only
                // discoverable once a chunk size is known.
                Warnings = [.. document.Warnings, .. chunkResult.Warnings]
            };
        });

    public Task<ConversionResult> GenerateChunksAsync(
        string filePath,
        string outputDirectory,
        ChunkOptions chunkOptions,
        CancellationToken cancellationToken) =>
        ExecuteAsync("Chunk generation", filePath, async () =>
        {
            var document = await _processorResolver.Resolve(filePath).ExtractAsync(filePath, cancellationToken);

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var chunkResult = await _chunkGenerator.GenerateChunksAsync(
                document, chunkOptions, $"{baseName}.md", cancellationToken);

            var chunksDirectory = Path.Combine(outputDirectory, "chunks", baseName);
            await _chunkFileWriter.WriteAsync(chunksDirectory, chunkResult.Chunks, cancellationToken);

            return new ConversionResult
            {
                Success = true,
                OutputPath = chunksDirectory,
                ChunkCount = chunkResult.Chunks.Count,
                CompletedStages = PipelineStage.Chunked,
                Metadata = document.Metadata,
                // Extraction warnings AND chunking warnings: the user is
                // chunking one document, and both kinds are about that
                // document's fitness for downstream use.
                Warnings = [.. document.Warnings, .. chunkResult.Warnings]
            };
        });

    // Shared error-to-result mapping (BR-006: a categorized failure for this
    // file only - the caller's batch loop, Phase 8, must continue with the
    // rest regardless of which operation failed).
    private async Task<ConversionResult> ExecuteAsync(
        string operationName,
        string filePath,
        Func<Task<ConversionResult>> action)
    {
        var fileName = Path.GetFileName(filePath);

        try
        {
            _logger.LogInformation("{Operation} started for {FileName}", operationName, fileName);
            var result = await action();
            _logger.LogInformation("{Operation} succeeded for {FileName}", operationName, fileName);
            return result;
        }
        catch (DocumentConversionException ex)
        {
            _logger.LogWarning(
                ex, "{Operation} failed for {FileName}: {Category}", operationName, fileName, ex.Category);
            return new ConversionResult { Success = false, Error = ex.Category, ErrorMessage = ex.Message };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during {Operation} for {FileName}", operationName, fileName);
            return new ConversionResult
            {
                Success = false,
                Error = ErrorCategory.UnexpectedException,
                ErrorMessage = $"An unexpected error occurred during {operationName.ToLowerInvariant()} for this file."
            };
        }
    }
}
