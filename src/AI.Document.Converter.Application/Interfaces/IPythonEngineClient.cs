using AI.Document.Converter.Application.DTOs;

namespace AI.Document.Converter.Application.Interfaces;

// ADR-001: starts the bundled Python engine as a short-lived subprocess per
// request, communicating via JSON over stdin/stdout, with timeout and
// cancellation (cancellation kills the process - FR-037).
public interface IPythonEngineClient
{
    // FR-038: verifies the configured engine is available and correctly
    // configured, used at startup before any file is imported.
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken);

    Task<PythonEngineResponse<TResult>> SendAsync<TResult>(
        PythonEngineRequest request,
        CancellationToken cancellationToken);
}
