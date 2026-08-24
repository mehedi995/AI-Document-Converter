using AI.Document.Converter.Application.DTOs;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;

namespace AI.Document.Converter.Infrastructure.Python;

public sealed class TokenCounter : ITokenCounter
{
    private readonly IPythonEngineClient _pythonEngineClient;

    public TokenCounter(IPythonEngineClient pythonEngineClient)
    {
        _pythonEngineClient = pythonEngineClient;
    }

    public async Task<IReadOnlyList<int>> CountBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var request = new PythonEngineRequest
        {
            Operation = "count_tokens",
            Payload = new CountTokensRequestPayload { Texts = texts }
        };

        var response = await _pythonEngineClient.SendAsync<TokenCountBatchResult>(request, cancellationToken);

        if (!response.Success || response.Result is null)
        {
            throw new DocumentConversionException(
                response.ErrorMessage ?? "Token counting failed.",
                response.ErrorCategory ?? ErrorCategory.PythonEngineFailure);
        }

        return response.Result.Counts;
    }
}
