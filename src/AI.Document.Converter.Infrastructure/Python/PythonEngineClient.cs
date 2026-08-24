using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Document.Converter.Application.DTOs;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Infrastructure.Python;

// ADR-001: one short-lived subprocess per request, JSON over stdin/stdout. The
// process is started with an argument-less ProcessStartInfo and UseShellExecute
// false (SEC-006) - the request never becomes part of a command line.
public sealed class PythonEngineClient : IPythonEngineClient
{
    // The wire format is plain camelCase JSON (neither language's native casing),
    // so the contract in ADR-001 doesn't silently depend on C#'s PascalCase
    // reflection defaults. ErrorCategory serializes as a camelCase string (e.g.
    // "pythonEngineFailure") so the Python side can read/write it without needing
    // to know the enum's underlying integer values.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IOptionsMonitor<AppSettings> _settingsMonitor;
    private readonly ILogger<PythonEngineClient> _logger;

    public PythonEngineClient(IOptionsMonitor<AppSettings> settingsMonitor, ILogger<PythonEngineClient> logger)
    {
        _settingsMonitor = settingsMonitor;
        _logger = logger;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<object>(
            new PythonEngineRequest { Operation = "health_check" },
            cancellationToken);

        return response.Success;
    }

    public async Task<PythonEngineResponse<TResult>> SendAsync<TResult>(
        PythonEngineRequest request,
        CancellationToken cancellationToken)
    {
        var executablePath = ResolveExecutablePath();

        if (!File.Exists(executablePath))
        {
            _logger.LogError("Python engine executable was not found at {ExecutablePath}", executablePath);
            return Failure<TResult>(
                ErrorCategory.PythonEngineFailure,
                "The document-processing engine could not be found. Check the Python engine path in Settings.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Failed to start Python engine at {ExecutablePath}", executablePath);
            return Failure<TResult>(
                ErrorCategory.PythonEngineFailure,
                "The document-processing engine could not be started.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settingsMonitor.CurrentValue.PythonEngineTimeoutSeconds));

        try
        {
            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await process.StandardInput.WriteLineAsync(requestJson);
            await process.StandardInput.FlushAsync(timeoutCts.Token);
            process.StandardInput.Close();

            var responseJson = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            var response = JsonSerializer.Deserialize<PythonEngineResponse<TResult>>(responseJson, JsonOptions);
            if (response is null)
            {
                return Failure<TResult>(
                    ErrorCategory.PythonEngineFailure,
                    "The document-processing engine returned an empty response.");
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User-initiated cancellation (FR-037) - kill the process and let the
            // cancellation propagate to the caller.
            KillProcessSafely(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            // The per-file timeout elapsed, not user cancellation.
            _logger.LogWarning("Python engine did not respond within the configured timeout");
            KillProcessSafely(process);
            return Failure<TResult>(
                ErrorCategory.PythonEngineFailure,
                "The document-processing engine did not respond in time.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Python engine response");
            return Failure<TResult>(
                ErrorCategory.PythonEngineFailure,
                "The document-processing engine returned an invalid response.");
        }
    }

    private string ResolveExecutablePath()
    {
        var configured = _settingsMonitor.CurrentValue.PythonExecutablePath;
        return string.IsNullOrWhiteSpace(configured) ? AppPaths.BundledPythonEnginePath : configured;
    }

    private static void KillProcessSafely(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill attempt.
        }
    }

    private static PythonEngineResponse<TResult> Failure<TResult>(ErrorCategory category, string message) =>
        new()
        {
            Success = false,
            ErrorCategory = category,
            ErrorMessage = message
        };
}
