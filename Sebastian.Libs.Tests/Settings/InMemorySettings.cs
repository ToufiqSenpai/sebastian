using Sebastian.Libs.Settings;

namespace Sebastian.Libs.Tests.Settings;

internal sealed class InMemorySettings : ISettings
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public bool Has(string key)
    {
        return _values.ContainsKey(key);
    }

    public string? Read(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    public void Write(string key, string value)
    {
        _values[key] = value;
    }

    public void Remove(string key)
    {
        _ = _values.Remove(key);
    }
}
