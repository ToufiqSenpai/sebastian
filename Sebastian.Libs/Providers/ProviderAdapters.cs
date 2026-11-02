using System.Collections.Frozen;
using Sebastian.Libs.Catalog;

namespace Sebastian.Libs.Providers;

public enum ProviderAdapter
{
    OpenAiCompatible = 0,
    Anthropic = 1,
    Azure = 2,
}

public readonly record struct BaseUrlValidation(
    bool IsValid,
    Uri? Url,
    string? Error);

public static class ProviderAdapters
{
    private static readonly FrozenDictionary<string, ProviderAdapter> AdaptersByNpm =
        new Dictionary<string, ProviderAdapter>(StringComparer.Ordinal)
        {
            ["@ai-sdk/openai-compatible"] = ProviderAdapter.OpenAiCompatible,
            ["@ai-sdk/openai"] = ProviderAdapter.OpenAiCompatible,
            ["@openrouter/ai-sdk-provider"] = ProviderAdapter.OpenAiCompatible,
            ["@ai-sdk/deepinfra"] = ProviderAdapter.OpenAiCompatible,
            ["@aihubmix/ai-sdk-provider"] = ProviderAdapter.OpenAiCompatible,
            ["@saladtechnologies-oss/ai-sdk-provider"] = ProviderAdapter.OpenAiCompatible,
            ["@jerome-benoit/sap-ai-provider-v2"] = ProviderAdapter.OpenAiCompatible,
            ["@ai-sdk/groq"] = ProviderAdapter.OpenAiCompatible,
            ["@ai-sdk/mistral"] = ProviderAdapter.OpenAiCompatible,
            ["@ai-sdk/xai"] = ProviderAdapter.OpenAiCompatible,
            ["@ai-sdk/perplexity"] = ProviderAdapter.OpenAiCompatible,
            ["@ai-sdk/anthropic"] = ProviderAdapter.Anthropic,
            ["@ai-sdk/azure"] = ProviderAdapter.Azure,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> DefaultBaseUrls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = "https://api.openai.com/v1",
            ["anthropic"] = "https://api.anthropic.com",
            ["groq"] = "https://api.groq.com/openai/v1",
            ["mistral"] = "https://api.mistral.ai/v1",
            ["xai"] = "https://api.x.ai/v1",
            ["google"] = "https://generativelanguage.googleapis.com/v1beta/openai/",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] AzureResourceNames =
    [
        "AZURE_RESOURCE_NAME",
        "AZURE_COGNITIVE_SERVICES_RESOURCE_NAME",
    ];

    public static ProviderAdapter? Infer(string? npm)
    {
        return string.IsNullOrWhiteSpace(npm) ? null : LookupAdapter(npm.Trim());
    }

    public static Uri? DefaultBaseUrl(string providerId)
    {
        return DefaultBaseUrls.TryGetValue(providerId, out var baseUrl)
            ? new Uri(baseUrl, UriKind.Absolute)
            : null;
    }

    public static Uri? ResolveBaseUrl(
        CatalogProvider provider,
        IReadOnlyDictionary<string, string>? env = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return !string.IsNullOrWhiteSpace(provider.Api)
            ? NormalizeBaseUrl(provider.Api)
            : ResolveDefaultBaseUrl(provider, env);
    }

    public static string? GetAzureResource(IReadOnlyDictionary<string, string>? env)
    {
        if (env is null)
        {
            return null;
        }

        foreach (var name in AzureResourceNames)
        {
            if (env.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    public static Uri? BuildAzureBaseUrl(string? resourceName)
    {
        return ValidateAzureResourceName(resourceName) is null
            ? new Uri($"https://{resourceName!.Trim()}.openai.azure.com/openai/v1", UriKind.Absolute)
            : null;
    }

    public static string? ValidateAzureResourceName(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            null or "" => "Enter the Azure resource name.",
            _ when trimmed.Length is >= 2 and <= 64 &&
                char.IsAsciiLetterOrDigit(trimmed[0]) &&
                char.IsAsciiLetterOrDigit(trimmed[^1]) &&
                trimmed.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character == '-') => null,
            _ => "Use only letters, numbers, and hyphens.",
        };
    }

    public static BaseUrlValidation ValidateBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new BaseUrlValidation(false, null, "Enter a base URL.");
        }

        var trimmed = value.Trim();
        return trimmed.Contains("${", StringComparison.Ordinal)
            ? new BaseUrlValidation(
                false,
                null,
                "Replace the template placeholder in the base URL.")
            : ValidateUri(trimmed);
    }

    public static Uri? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('/');
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ? uri : null;
    }

    public static string ToText(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return url.OriginalString.TrimEnd('/');
    }

    public static string? DocumentedHost(
        CatalogProvider provider,
        IReadOnlyDictionary<string, string>? env = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var candidates = new[]
        {
            NormalizeBaseUrl(provider.Api),
            ResolveBaseUrl(provider, env),
            NormalizeBaseUrl(provider.Doc),
        };

        foreach (var candidate in candidates)
        {
            if (candidate is not null)
            {
                return candidate.Host;
            }
        }

        return null;
    }

    public static bool NeedsHostConfirmation(
        Uri? baseUrl,
        CatalogProvider provider,
        IReadOnlyDictionary<string, string>? env = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (baseUrl is null)
        {
            return false;
        }

        var documented = DocumentedHost(provider, env);
        return documented is null ||
            !string.Equals(baseUrl.Host, documented, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToApi(ProviderAdapter adapter)
    {
        return adapter switch
        {
            ProviderAdapter.OpenAiCompatible => "openai",
            ProviderAdapter.Anthropic => "anthropic",
            ProviderAdapter.Azure => "azure",
            _ => throw new ArgumentOutOfRangeException(nameof(adapter)),
        };
    }

    public static ProviderAdapter? ParseApi(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant() switch
            {
                "ANTHROPIC" => ProviderAdapter.Anthropic,
                "AZURE" => ProviderAdapter.Azure,
                "OPENAI" => ProviderAdapter.OpenAiCompatible,
                _ => null,
            };
    }

    public static string Label(ProviderAdapter adapter)
    {
        return adapter switch
        {
            ProviderAdapter.OpenAiCompatible => "OpenAI-compatible",
            ProviderAdapter.Anthropic => "Anthropic",
            ProviderAdapter.Azure => "Azure",
            _ => throw new ArgumentOutOfRangeException(nameof(adapter)),
        };
    }

    public static int ToIndex(ProviderAdapter adapter)
    {
        return (int)adapter;
    }

    public static ProviderAdapter FromIndex(int index)
    {
        return Enum.IsDefined(typeof(ProviderAdapter), index)
            ? (ProviderAdapter)index
            : ProviderAdapter.OpenAiCompatible;
    }

    private static ProviderAdapter? LookupAdapter(string npm)
    {
        return AdaptersByNpm.TryGetValue(npm, out var adapter) ? adapter : null;
    }

    private static Uri? ResolveDefaultBaseUrl(
        CatalogProvider provider,
        IReadOnlyDictionary<string, string>? env)
    {
        return Infer(provider.Npm) == ProviderAdapter.Azure
            ? BuildAzureBaseUrl(GetAzureResource(env))
            : DefaultBaseUrl(provider.Id);
    }

    private static BaseUrlValidation ValidateScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            ? new BaseUrlValidation(true, uri, Error: null)
            : new BaseUrlValidation(
                false,
                null,
                "Use HTTPS. HTTP is allowed only for localhost.");
    }

    private static BaseUrlValidation ValidateParsedUri(Uri uri)
    {
        return !string.IsNullOrEmpty(uri.UserInfo)
            ? new BaseUrlValidation(
                false,
                null,
                "A base URL cannot include credentials.")
            : ValidateScheme(uri);
    }

    private static BaseUrlValidation ValidateUri(string value)
    {
        var normalized = NormalizeBaseUrl(value);
        return normalized is not null
            ? ValidateParsedUri(normalized)
            : new BaseUrlValidation(false, null, "Enter an absolute URL.");
    }
}
