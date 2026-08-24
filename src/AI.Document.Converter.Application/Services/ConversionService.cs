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
        ExecuteAsync("Conversion", filePath, async () =>
        {
            var document = await _processorResolver.Resolve(filePath).ExtractAsync(filePath, cancellationToken);

            var markdown = _markdownGenerator.Generate(document);
            var plainText = PlainTextRenderer.Render(document);
            var tokens = await _tokenEstimator.EstimateAsync(plainText, markdown, cancellationToken);

            var outputPath = outputPathResolver.ResolveMarkdownOutputPath(filePath, outputDirectory);
            await _markdownFileWriter.WriteAsync(outputPath, markdown, cancellationToken);

            return new ConversionResult
            {
                Success = true,
                OutputPath = outputPath,
                Tokens = tokens,
                CompletedStages = PipelineStage.Converted,
                Metadata = document.Metadata
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
            var chunks = await _chunkGenerator.GenerateChunksAsync(
                document, chunkOptions, $"{baseName}.md", cancellationToken);

            var chunksDirectory = Path.Combine(outputDirectory, "chunks", baseName);
            await _chunkFileWriter.WriteAsync(chunksDirectory, chunks, cancellationToken);

            return new ConversionResult
            {
                Success = true,
                OutputPath = chunksDirectory,
                ChunkCount = chunks.Count,
                CompletedStages = PipelineStage.Chunked
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
