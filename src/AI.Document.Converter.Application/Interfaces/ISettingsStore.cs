using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Interfaces;

// Persistence-only contract (FR-032/FR-033). Validation (e.g., SEC-005 Python-path
// checks) belongs in a higher-level service, not here.
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
