using Microsoft.Extensions.Options;

namespace AI.Document.Converter.IntegrationTests.TestSupport;

// Same minimal test double as the unit test project - not shared via a project
// reference between the two independent test projects to avoid coupling them.
public sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T currentValue)
    {
        CurrentValue = currentValue;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => new NoOpDisposable();

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
