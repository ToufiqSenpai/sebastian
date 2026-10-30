namespace Sebastian.Configuration;

internal static class EnvironmentPlaceholders
{
    private const string Prefix = "{env:";
    private const char Suffix = '}';

    // Values are still text here, so int settings such as GitHub:AppId work too.
    // shortcut: whole-value placeholders only; extend if inline interpolation is needed.
    public static void ExpandEnvironmentPlaceholders(this ConfigurationManager configuration)
    {
        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in configuration.AsEnumerable())
        {
            if (value is null
                || !value.StartsWith(Prefix, StringComparison.Ordinal)
                || !value.EndsWith(Suffix))
            {
                continue;
            }

            var name = value[Prefix.Length..^1];
            var environmentValue = name.Length == 0 ? null : Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(environmentValue))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{name}' for configuration key '{key}' is not set.");
            }

            resolved[key] = environmentValue;
        }

        configuration.AddInMemoryCollection(resolved);
    }
}
