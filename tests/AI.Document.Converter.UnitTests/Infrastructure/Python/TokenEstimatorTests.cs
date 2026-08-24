using AI.Document.Converter.Application.DTOs;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.Python;
using Moq;
using Xunit;

namespace AI.Document.Converter.UnitTests.Infrastructure.Python;

public class TokenEstimatorTests
{
    private readonly Mock<IPythonEngineClient> _pythonEngineClient = new();
    private readonly TokenEstimator _estimator;

    public TokenEstimatorTests()
    {
        _estimator = new TokenEstimator(_pythonEngineClient.Object);
    }

    [Fact]
    public async Task EstimateAsync_SuccessfulResponse_ReturnsTokenEstimate()
    {
        var expected = new TokenEstimate
        {
            OriginalClaudeStyle = 100,
            ConvertedClaudeStyle = 50,
            OriginalGpt4oStyle = 95,
            ConvertedGpt4oStyle = 48
        };

        _pythonEngineClient
            .Setup(c => c.SendAsync<TokenEstimate>(
                It.Is<PythonEngineRequest>(r => r.Operation == "tokenize"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PythonEngineResponse<TokenEstimate> { Success = true, Result = expected });

        var result = await _estimator.EstimateAsync("original", "converted", CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task EstimateAsync_SendsBothOriginalAndConvertedText()
    {
        PythonEngineRequest? capturedRequest = null;

        _pythonEngineClient
            .Setup(c => c.SendAsync<TokenEstimate>(It.IsAny<PythonEngineRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PythonEngineRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new PythonEngineResponse<TokenEstimate>
            {
                Success = true,
                Result = new TokenEstimate
                {
                    OriginalClaudeStyle = 1,
                    ConvertedClaudeStyle = 1,
                    OriginalGpt4oStyle = 1,
                    ConvertedGpt4oStyle = 1
                }
            });

        await _estimator.EstimateAsync("the original text", "the converted markdown", CancellationToken.None);

        var payload = Assert.IsType<TokenizeRequestPayload>(capturedRequest!.Payload);
        Assert.Equal("the original text", payload.OriginalText);
        Assert.Equal("the converted markdown", payload.ConvertedText);
    }

    [Fact]
    public async Task EstimateAsync_FailedResponse_ThrowsWithReportedCategory()
    {
        _pythonEngineClient
            .Setup(c => c.SendAsync<TokenEstimate>(It.IsAny<PythonEngineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PythonEngineResponse<TokenEstimate>
            {
                Success = false,
                ErrorCategory = ErrorCategory.PythonEngineFailure,
                ErrorMessage = "boom"
            });

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => _estimator.EstimateAsync("a", "b", CancellationToken.None));

        Assert.Equal(ErrorCategory.PythonEngineFailure, exception.Category);
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task EstimateAsync_NullResultDespiteSuccess_ThrowsPythonEngineFailure()
    {
        _pythonEngineClient
            .Setup(c => c.SendAsync<TokenEstimate>(It.IsAny<PythonEngineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PythonEngineResponse<TokenEstimate> { Success = true, Result = null });

        var exception = await Assert.ThrowsAsync<DocumentConversionException>(
            () => _estimator.EstimateAsync("a", "b", CancellationToken.None));

        Assert.Equal(ErrorCategory.PythonEngineFailure, exception.Category);
    }
}
