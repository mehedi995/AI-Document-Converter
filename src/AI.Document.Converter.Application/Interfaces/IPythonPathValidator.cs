namespace AI.Document.Converter.Application.Interfaces;

// SEC-005: the configured Python executable path must be validated (exists, is
// executable, resolves to a genuine Python interpreter) before it is ever invoked.
public interface IPythonPathValidator
{
    Task<bool> IsValidAsync(string pythonExecutablePath, CancellationToken cancellationToken);
}
