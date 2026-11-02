using Sebastian.Libs.Catalog;
using Sebastian.Libs.Settings;

namespace Sebastian.Libs.Providers;

public sealed record ProviderSelection(
    CatalogProvider Provider,
    ProviderAdapter Adapter,
    Uri BaseUrl,
    bool IsCustomEndpoint,
    IReadOnlyDictionary<string, string> Env);

public sealed record ProviderStoredState(
    string? ProviderId,
    string? Api,
    Uri? BaseUrl,
    bool HasApiKey,
    IReadOnlyDictionary<string, string> PlainEnv);

public sealed class ProviderSetupService
{
    private readonly KeyValueStore _store;
    private readonly ProviderTester _tester;

    public ProviderSetupService(KeyValueStore store, ProviderTester tester)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tester);

        _store = store;
        _tester = tester;
    }

    public string? SelectedProviderId => _store["agent:provider"];

    public ProviderStoredState GetStoredState(CatalogProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var plainEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in ProviderFields.Build(provider))
        {
            if (field.IsPrimaryKey || field.IsSecret)
            {
                continue;
            }

            var value = _store[EnvSetting(provider.Id, field.Name)];
            if (!string.IsNullOrWhiteSpace(value))
            {
                plainEnv[field.Name] = value;
            }
        }

        return new ProviderStoredState(
            SelectedProviderId,
            _store[ApiSetting(provider.Id)],
            ProviderAdapters.NormalizeBaseUrl(_store[BaseUrlSetting(provider.Id)]),
            _store.Has(ApiKeySetting(provider.Id)),
            plainEnv);
    }

    public async Task<ProviderTestResult> TestAndSaveAsync(
        ProviderSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var provider = selection.Provider;
        var primary = ProviderFields.PrimaryKey(provider);
        if (primary is null)
        {
            return ProviderTestResult.Failed("This provider has no credential field.");
        }

        var candidateKey = Value(selection.Env, primary.Name);
        var apiKey = candidateKey ?? _store[ApiKeySetting(provider.Id)];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderTestResult.Failed("Enter an API key.");
        }

        var result = await _tester
            .TestAsync(
                selection.Adapter,
                selection.BaseUrl,
                apiKey,
                provider.Models,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(candidateKey))
        {
            _store.Set(ApiKeySetting(provider.Id), candidateKey, isSecret: true);
        }

        SaveFields(provider, selection.Env);
        SaveEndpoint(selection);

        _store.Set("agent:provider", provider.Id, isSecret: false);
        return result;
    }

    private void SaveFields(CatalogProvider provider, IReadOnlyDictionary<string, string> env)
    {
        foreach (var field in ProviderFields.Build(provider))
        {
            if (field.IsPrimaryKey)
            {
                continue;
            }

            var value = Value(env, field.Name);
            var key = EnvSetting(provider.Id, field.Name);

            if (field.IsSecret)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _store.Set(key, value, isSecret: true);
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                _store.Remove(key);
            }
            else
            {
                _store.Set(key, value, isSecret: false);
            }
        }
    }

    private void SaveEndpoint(ProviderSelection selection)
    {
        if (selection.IsCustomEndpoint)
        {
            _store.Set(
                ApiSetting(selection.Provider.Id),
                ProviderAdapters.ToApi(selection.Adapter),
                isSecret: false);
        }
        else
        {
            _store.Remove(ApiSetting(selection.Provider.Id));
        }

        var defaultBase = ProviderAdapters.ResolveBaseUrl(selection.Provider, selection.Env);

        if (selection.IsCustomEndpoint ||
            !Uri.Equals(selection.BaseUrl, defaultBase))
        {
            _store.Set(
                BaseUrlSetting(selection.Provider.Id),
                ProviderAdapters.ToText(selection.BaseUrl),
                isSecret: false);
        }
        else
        {
            _store.Remove(BaseUrlSetting(selection.Provider.Id));
        }
    }

    private static string? Value(IReadOnlyDictionary<string, string> env, string name)
    {
        return env.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static string ApiKeySetting(string providerId)
    {
        return $"agent:{providerId}:api-key";
    }

    private static string ApiSetting(string providerId)
    {
        return $"agent:{providerId}:api";
    }

    private static string BaseUrlSetting(string providerId)
    {
        return $"agent:{providerId}:base-url";
    }

    private static string EnvSetting(string providerId, string envName)
    {
        return $"agent:{providerId}:env:{envName}";
    }
}
