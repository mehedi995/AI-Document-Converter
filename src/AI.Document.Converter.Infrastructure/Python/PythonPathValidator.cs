using AI.Document.Converter.Application.Interfaces;

namespace AI.Document.Converter.Infrastructure.Python;

// SEC-005: cheap, synchronous-feeling checks (exists, is an .exe) done before a
// candidate path is ever saved to Settings. The deeper check - that the
// executable actually speaks the expected JSON protocol - happens naturally the
// first time IPythonEngineClient uses the now-saved path (FR-038's startup health
// check), rather than being duplicated here.
public sealed class PythonPathValidator : IPythonPathValidator
{
    public Task<bool> IsValidAsync(string pythonExecutablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pythonExecutablePath) || !File.Exists(pythonExecutablePath))
        {
            return Task.FromResult(false);
        }

        var isExecutable = string.Equals(
            Path.GetExtension(pythonExecutablePath),
            ".exe",
            StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(isExecutable);
    }
}
