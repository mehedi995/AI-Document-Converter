using AI.Document.Converter.Application.DTOs;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Infrastructure.Python;

public sealed class TokenEstimator : ITokenEstimator
{
    private readonly IPythonEngineClient _pythonEngineClient;

    public TokenEstimator(IPythonEngineClient pythonEngineClient)
    {
        _pythonEngineClient = pythonEngineClient;
    }

    public async Task<TokenEstimate> EstimateAsync(
        string originalText,
        string convertedMarkdown,
        CancellationToken cancellationToken)
    {
        var request = new PythonEngineRequest
        {
            Operation = "tokenize",
            Payload = new TokenizeRequestPayload
            {
                OriginalText = originalText,
                ConvertedText = convertedMarkdown
            }
        };

        var response = await _pythonEngineClient.SendAsync<TokenEstimate>(request, cancellationToken);

        if (!response.Success || response.Result is null)
        {
            throw new DocumentConversionException(
                response.ErrorMessage ?? "Token estimation failed.",
                response.ErrorCategory ?? ErrorCategory.PythonEngineFailure);
        }

        return response.Result;
    }
}
