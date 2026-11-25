using Microsoft.Extensions.Options;

namespace Bassign.UnitTests;

/// <summary>An IOptionsMonitor whose value a test can change, notifying listeners like a config reload does.</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    public T CurrentValue { get; private set; } = value;
    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return null;
    }

    public void Set(T next)
    {
        CurrentValue = next;
        foreach (var l in _listeners) l(next, null);
    }
}
