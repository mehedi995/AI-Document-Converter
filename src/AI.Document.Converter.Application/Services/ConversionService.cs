using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace AI.Document.Converter.Application.Services;

public sealed class ConversionService : IConversionService
{
    private readonly IDocumentProcessorResolver _processorResolver;
    private readonly IMarkdownGenerator _markdownGenerator;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IMarkdownFileWriter _markdownFileWriter;
    private readonly ILogger<ConversionService> _logger;

    public ConversionService(
        IDocumentProcessorResolver processorResolver,
        IMarkdownGenerator markdownGenerator,
        ITokenEstimator tokenEstimator,
        IMarkdownFileWriter markdownFileWriter,
        ILogger<ConversionService> logger)
    {
        _processorResolver = processorResolver;
        _markdownGenerator = markdownGenerator;
        _tokenEstimator = tokenEstimator;
        _markdownFileWriter = markdownFileWriter;
        _logger = logger;
    }

    public async Task<ConversionResult> ConvertAsync(
        string filePath,
        string outputDirectory,
        IOutputPathResolver outputPathResolver,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);

        try
        {
            _logger.LogInformation("Conversion started for {FileName}", fileName);

            var processor = _processorResolver.Resolve(filePath);
            var document = await processor.ExtractAsync(filePath, cancellationToken);

            var markdown = _markdownGenerator.Generate(document);
            var plainText = PlainTextRenderer.Render(document);
            var tokens = await _tokenEstimator.EstimateAsync(plainText, markdown, cancellationToken);

            var outputPath = outputPathResolver.ResolveMarkdownOutputPath(filePath, outputDirectory);
            await _markdownFileWriter.WriteAsync(outputPath, markdown, cancellationToken);

            _logger.LogInformation("Conversion succeeded for {FileName}", fileName);

            return new ConversionResult
            {
                Success = true,
                OutputPath = outputPath,
                Tokens = tokens,
                CompletedStages = PipelineStage.Converted
            };
        }
        catch (DocumentConversionException ex)
        {
            // BR-006: a categorized failure for this file only - the caller
            // (a future batch loop) must continue with the rest.
            _logger.LogWarning(ex, "Conversion failed for {FileName}: {Category}", fileName, ex.Category);
            return new ConversionResult
            {
                Success = false,
                Error = ex.Category,
                ErrorMessage = ex.Message
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error converting {FileName}", fileName);
            return new ConversionResult
            {
                Success = false,
                Error = ErrorCategory.UnexpectedException,
                ErrorMessage = "An unexpected error occurred while converting this file."
            };
        }
    }
}
