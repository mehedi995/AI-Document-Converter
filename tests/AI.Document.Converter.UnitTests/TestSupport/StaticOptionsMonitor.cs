using Microsoft.Extensions.Options;

namespace AI.Document.Converter.UnitTests.TestSupport;

// A minimal IOptionsMonitor<T> test double - fixed value, no reload support -
// enough for unit tests that only need CurrentValue.
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
