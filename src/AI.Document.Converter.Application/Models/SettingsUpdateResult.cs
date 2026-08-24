namespace AI.Document.Converter.Application.Models;

// UC-005: on an invalid setting, the caller shows this message and retains the
// previous valid value - it does not throw.
public sealed class SettingsUpdateResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }
}
