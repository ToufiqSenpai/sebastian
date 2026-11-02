using System.Diagnostics.CodeAnalysis;
using Sebastian.Libs.Catalog;

namespace Sebastian.Libs.Providers;

public sealed record ProviderField(
    string Name,
    string Label,
    bool IsSecret,
    bool IsPrimaryKey,
    IReadOnlyList<string> Aliases);

public static class ProviderFields
{
    private static readonly string[] SecretMarkers =
    [
        "KEY",
        "TOKEN",
        "SECRET",
        "PAT",
        "PASSWORD",
        "CREDENTIAL",
        "CONNECTION_STRING",
    ];

    public static IReadOnlyList<ProviderField> Build(CatalogProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var env = provider.Env
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (env.Length == 0)
        {
            return
            [
                new ProviderField(
                    "API_KEY",
                    "API key",
                    IsSecret: true,
                    IsPrimaryKey: true,
                    []),
            ];
        }

        var secrets = env.Where(IsSecretName).ToArray();
        var plain = env.Where(name => !IsSecretName(name)).ToArray();

        if (secrets.Length > 1 && plain.Length == 0)
        {
            return
            [
                new ProviderField(
                    secrets[0],
                    "API key",
                    IsSecret: true,
                    IsPrimaryKey: true,
                    secrets),
            ];
        }

        var primary = secrets.FirstOrDefault();
        var fields = new List<ProviderField>(env.Length);

        foreach (var name in env)
        {
            var isSecret = IsSecretName(name);
            fields.Add(new ProviderField(
                name,
                isSecret && name == primary ? "API key" : ToLabel(name),
                isSecret,
                isSecret && name == primary,
                []));
        }

        return fields;
    }

    public static ProviderField? PrimaryKey(CatalogProvider provider)
    {
        return Build(provider).FirstOrDefault(field => field.IsPrimaryKey);
    }

    public static bool IsSecretName(string envName)
    {
        return SecretMarkers.Any(
            marker => envName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static string ToLabel(string envName)
    {
        ArgumentNullException.ThrowIfNull(envName);

        var words = envName.Split(
            '_',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', words.Select(FormatWord));
    }

    [SuppressMessage(
        "Globalization",
        "CA1308",
        Justification = "Field labels use title case, not normalized uppercase.")]
    private static string FormatWord(string word)
    {
        return word.ToUpperInvariant() switch
        {
            "API" => "API",
            "ID" => "ID",
            "URL" => "URL",
            "URI" => "URI",
            "AWS" => "AWS",
            "GCP" => "GCP",
            "AI" => "AI",
            "PAT" => "PAT",
            _ => string.Concat(char.ToUpperInvariant(word[0]), word[1..].ToLowerInvariant()),
        };
    }
}
