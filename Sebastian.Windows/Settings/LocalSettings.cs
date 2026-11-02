using System.Diagnostics.CodeAnalysis;
using Windows.Storage;
using Sebastian.Libs.Settings;

namespace Sebastian.Windows.Settings;

[SuppressMessage("Performance", "CA1812", Justification = "Created by the DI container.")]
internal sealed class LocalSettings : ISettings
{
    public bool Has(string key)
    {
        return ApplicationData.Current.LocalSettings.Values.ContainsKey(key);
    }

    public string? Read(string key)
    {
        return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var value)
            ? value as string
            : null;
    }

    public void Write(string key, string value)
    {
        ApplicationData.Current.LocalSettings.Values[key] = value;
    }

    public void Remove(string key)
    {
        _ = ApplicationData.Current.LocalSettings.Values.Remove(key);
    }
}
