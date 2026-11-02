using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.Providers;
using AppContexts = Sebastian.Windows.Bootstrap.AppContexts;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal sealed record ProviderStepProps(
    Action Back,
    Action<ProviderTestResult> Next,
    double ContentWidth,
    GitHubAppValidation? App);

internal sealed record ProviderEndpoint(
    ProviderAdapter Adapter,
    bool DerivedBaseUrl,
    string? DefaultBaseText,
    string EffectiveBaseUrl);

internal sealed record ProviderHostState(
    string? DestinationHost,
    bool NeedsConfirmation,
    bool Confirmed);

internal sealed class ProviderStep : Component<ProviderStepProps>
{
    public override Element Render()
    {
        var catalogService = UseContext(AppContexts.Catalog);
        var providerSetup = UseContext(AppContexts.Providers);
        var wide = UseBreakpoint(720);
        (var _, var windowHeight) = UseWindowSize();
        var pageHeight = SetupLayout.PageHeight(windowHeight);

        (var attempt, var setAttempt) = UseState(0);
        var catalog = UseResource(
            catalogService.GetAsync,
            [attempt]);

        (var providerId, var setProviderId) = UseState(providerSetup.SelectedProviderId ?? "");
        (var baseUrl, var setBaseUrl) = UseState("");
        (var customEndpoint, var setCustomEndpoint) = UseState(false);
        (var adapterIndex, var setAdapterIndex) = UseState(0);
        (var confirmedHost, var setConfirmedHost) = UseState<string?>(null);
        (var envValues, var updateEnv) = UseReducer(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var initializedProvider = UseRef<string?>(null);

        var test = UseMutation<ProviderSelection, ProviderTestResult>(
            providerSetup.TestAndSaveAsync);

        var catalogData = CatalogData(catalog);
        var catalogError = catalog is AsyncValue<ModelCatalog>.Error error
            ? error.Exception
            : null;
        var provider = catalogData?.FindProvider(providerId);
        var inferredAdapter = InferAdapter(provider);

        UseEffect(
            () =>
            {
                if (provider is null || initializedProvider.Current == provider.Id)
                {
                    return;
                }

                var stored = providerSetup.GetStoredState(provider);
                var adapter = ProviderAdapters.ParseApi(stored.Api) ?? inferredAdapter;

                setBaseUrl(
                    stored.BaseUrl is null ? string.Empty : ProviderAdapters.ToText(stored.BaseUrl));
                setCustomEndpoint(stored.Api is not null);
                setAdapterIndex(ProviderAdapters.ToIndex(adapter));
                updateEnv(
                    _ => new Dictionary<string, string>(
                        stored.PlainEnv,
                        StringComparer.OrdinalIgnoreCase));
                setConfirmedHost(null);
                test.Reset();
                initializedProvider.Current = provider.Id;
            },
            provider);

        var fields = FieldsFor(provider);
        var primary = PrimaryKeyFor(provider);
        var apiKey = ResolveApiKey(primary, envValues);
        var storedState = StoredFor(provider, providerSetup);
        var endpoint = DeriveEndpoint(
            provider,
            inferredAdapter,
            customEndpoint,
            adapterIndex,
            baseUrl,
            envValues);
        var validation = DeriveValidation(provider, endpoint, customEndpoint, envValues);
        var host = DeriveHost(provider, endpoint, validation, envValues, confirmedHost);
        var canTest = CanTest(provider, primary, storedState, apiKey, validation, host, test.IsPending);

        void SelectProvider(CatalogProvider selected)
        {
            initializedProvider.Current = null;
            setProviderId(selected.Id);
            setBaseUrl("");
            setCustomEndpoint(false);
            setAdapterIndex(0);
            setConfirmedHost(null);
            updateEnv(_ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            test.Reset();
        }

        void UpdateBaseUrl(string value)
        {
            setBaseUrl(value);
            setConfirmedHost(null);
            test.Reset();
        }

        void UpdateEnvValue(string name, string value)
        {
            updateEnv(
                current => new Dictionary<string, string>(
                    current,
                    StringComparer.OrdinalIgnoreCase)
                {
                    [name] = value,
                });
            setConfirmedHost(null);
            test.Reset();
        }

        void SetCustom(bool value)
        {
            setCustomEndpoint(value);
            if (value && provider is not null)
            {
                setAdapterIndex(ProviderAdapters.ToIndex(inferredAdapter));
            }

            setConfirmedHost(null);
            test.Reset();
        }

        void SetAdapter(int index)
        {
            setAdapterIndex(index);
            setConfirmedHost(null);
            test.Reset();
        }

        async Task SubmitAsync()
        {
            var selection = BuildSelection(
                canTest,
                test.IsPending,
                provider,
                primary,
                endpoint,
                validation,
                customEndpoint,
                envValues);
            if (selection is null)
            {
                return;
            }

            var result = await RunTestAsync(test, selection).ConfigureAwait(true);
            if (result?.Success is true)
            {
                Props.Next(result);
            }
        }

        var actions = new ProviderDetailsActions(
            UpdateBaseUrl,
            UpdateEnvValue,
            SetCustom,
            SetAdapter,
            setConfirmedHost);

        var picker = Component<ProviderPicker, ProviderPickerProps>(
            new ProviderPickerProps(
                catalogData,
                catalogError,
                providerId,
                provider?.Name ?? "Select a provider",
                test.IsPending,
                Props.ContentWidth,
                wide,
                SelectProvider,
                () => setAttempt(attempt + 1)));

        var details = Component<ProviderDetails, ProviderDetailsProps>(
            new ProviderDetailsProps(
                new ProviderDetailsState(
                    provider,
                    fields,
                    envValues,
                    storedState,
                    endpoint,
                    validation,
                    host.DestinationHost,
                    host.NeedsConfirmation,
                    host.Confirmed,
                    apiKey,
                    baseUrl,
                    inferredAdapter,
                    adapterIndex,
                    customEndpoint,
                    test.IsPending),
                actions));

        var testStatus = BuildTestStatus(test);
        var header = VStack(
            0,
            Title("Choose a provider")
                .TextWrapping(TextWrapping.Wrap)
                .HeadingLevel(AutomationHeadingLevel.Level1),
            Body("Pick a model provider, then verify its API key.")
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.Wrap)
                .Margin(0, 8, 0, 0));

        var nextButton = Button("Next", () => _ = SubmitAsync())
            .AccentButton()
            .IsEnabled(canTest)
            .AutomationName("Test provider and continue");
        var backButton = Button("Back", Props.Back)
            .SubtleButton()
            .AutomationName("Back to the GitHub App step");

        var actionsRow = SetupLayout.Actions(wide, backButton, nextButton);

        return Grid(
            [GridSize.Star()],
            [GridSize.Star(), GridSize.Auto],
            [
                ScrollView(
                        VStack(
                            0,
                            header,
                            BuildAppInfo(Props.App).Margin(0, 16, 0, 0),
                            VStack(0, picker, details.Margin(0, 20, 0, 0))
                                .Margin(0, 24, 0, 0)))
                    .Grid(row: 0, column: 0),
                VStack(
                    0,
                    testStatus,
                    actionsRow.Margin(0, 12, 0, 0))
                    .Grid(row: 1, column: 0),
            ])
            .Height(pageHeight);
    }

    private static ProviderSelection? BuildSelection(
        bool canTest,
        bool pending,
        CatalogProvider? provider,
        ProviderField? primary,
        ProviderEndpoint endpoint,
        BaseUrlValidation validation,
        bool customEndpoint,
        Dictionary<string, string> env)
    {
        return !canTest ||
            pending ||
            provider is null ||
            primary is null ||
            validation.Url is null
            ? null
            : new ProviderSelection(provider, endpoint.Adapter, validation.Url, customEndpoint, env);
    }

    private static async Task<ProviderTestResult?> RunTestAsync(
        Mutation<ProviderSelection, ProviderTestResult> test,
        ProviderSelection selection)
    {
        try
        {
            return await test.RunAsync(selection).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static ModelCatalog? CatalogData(AsyncValue<ModelCatalog> catalog)
    {
        return catalog switch
        {
            AsyncValue<ModelCatalog>.Data data => data.Value,
            AsyncValue<ModelCatalog>.Reloading reloading => reloading.Previous,
            _ => null,
        };
    }

    private static ProviderAdapter InferAdapter(CatalogProvider? provider)
    {
        return provider is null
            ? ProviderAdapter.OpenAiCompatible
            : ProviderAdapters.Infer(provider.Npm) ?? ProviderAdapter.OpenAiCompatible;
    }

    private static ProviderField[] FieldsFor(CatalogProvider? provider)
    {
        return provider is null
            ? []
            : [.. ProviderFields.Build(provider)];
    }

    private static ProviderField? PrimaryKeyFor(CatalogProvider? provider)
    {
        return provider is null ? null : ProviderFields.PrimaryKey(provider);
    }

    private static string? ResolveApiKey(
        ProviderField? primary,
        Dictionary<string, string> env)
    {
        return primary switch
        {
            null => null,
            _ => env.GetValueOrDefault(primary.Name),
        };
    }

    private static ProviderStoredState? StoredFor(
        CatalogProvider? provider,
        ProviderSetupService setup)
    {
        return provider is null ? null : setup.GetStoredState(provider);
    }

    private static ProviderEndpoint DeriveEndpoint(
        CatalogProvider? provider,
        ProviderAdapter inferredAdapter,
        bool customEndpoint,
        int adapterIndex,
        string baseUrl,
        IReadOnlyDictionary<string, string> env)
    {
        var adapter = customEndpoint
            ? ProviderAdapters.FromIndex(adapterIndex)
            : inferredAdapter;
        var defaultBaseUrl = provider is null
            ? null
            : ProviderAdapters.ResolveBaseUrl(provider, env);
        var defaultBaseText = defaultBaseUrl is null
            ? null
            : ProviderAdapters.ToText(defaultBaseUrl);
        var effectiveBaseUrl = (provider, adapter, customEndpoint) switch
        {
            (null, _, _) => string.Empty,
            (_, ProviderAdapter.Azure, false) => defaultBaseText ?? string.Empty,
            _ => string.IsNullOrWhiteSpace(baseUrl)
                ? defaultBaseText ?? string.Empty
                : baseUrl,
        };
        var derivedBaseUrl = adapter == ProviderAdapter.Azure && !customEndpoint;

        return new ProviderEndpoint(adapter, derivedBaseUrl, defaultBaseText, effectiveBaseUrl);
    }

    private static BaseUrlValidation DeriveValidation(
        CatalogProvider? provider,
        ProviderEndpoint endpoint,
        bool customEndpoint,
        IReadOnlyDictionary<string, string> env)
    {
        var resourceError = provider is not null &&
            endpoint.Adapter == ProviderAdapter.Azure &&
            !customEndpoint
            ? ProviderAdapters.ValidateAzureResourceName(
                ProviderAdapters.GetAzureResource(env))
            : null;

        return resourceError is null
            ? ProviderAdapters.ValidateBaseUrl(endpoint.EffectiveBaseUrl)
            : new BaseUrlValidation(false, null, resourceError);
    }

    private static ProviderHostState DeriveHost(
        CatalogProvider? provider,
        ProviderEndpoint endpoint,
        BaseUrlValidation validation,
        IReadOnlyDictionary<string, string> env,
        string? confirmedHost)
    {
        var destinationHost = Uri.TryCreate(
            endpoint.EffectiveBaseUrl,
            UriKind.Absolute,
            out var destinationUri)
            ? destinationUri.Host
            : null;
        var needsConfirmation = provider is not null &&
            destinationHost is not null &&
            ProviderAdapters.NeedsHostConfirmation(validation.Url, provider, env);
        var confirmed = destinationHost is not null &&
            string.Equals(confirmedHost, destinationHost, StringComparison.OrdinalIgnoreCase);

        return new ProviderHostState(destinationHost, needsConfirmation, confirmed);
    }

    private static bool CanTest(
        CatalogProvider? provider,
        ProviderField? primary,
        ProviderStoredState? stored,
        string? apiKey,
        BaseUrlValidation validation,
        ProviderHostState host,
        bool pending)
    {
        var hasKey = !string.IsNullOrWhiteSpace(apiKey) || stored?.HasApiKey is true;

        return provider is not null &&
            primary is not null &&
            validation.IsValid &&
            hasKey &&
            !pending &&
            (!host.NeedsConfirmation || host.Confirmed);
    }

    private static Element BuildTestStatus(Mutation<ProviderSelection, ProviderTestResult> test)
    {
        return test switch
        {
            { IsPending: true } => HStack(
                8,
                ProgressRing().Width(16).Height(16),
                Body("Testing…").Foreground(Theme.SecondaryText)),
            { Error: { } failure } => InfoBar(
                    "Could not verify the provider",
                    failure.Message)
                .Error()
                .IsClosable(false),
            { LastResult: { Success: false } failed } => InfoBar(
                    "Could not verify the provider",
                    failed.Error ?? "The provider test failed.")
                .Error()
                .IsClosable(false),
            { LastResult: { Success: true } passed } => InfoBar(
                    "Connected",
                    passed.UsedFallback
                        ? "The model list was unavailable; the key was verified with a chat completion."
                        : $"{passed.ModelCount} models available.")
                .Success()
                .IsClosable(false),
            _ => Empty(),
        };
    }

    private static Element BuildAppInfo(GitHubAppValidation? app)
    {
        return app switch
        {
            { Success: true, RepositoryCount: 0 } connected => InfoBar(
                    "GitHub App connected",
                    $"The installation covers no repositories. Install it: {connected.InstallUrl}")
                .Warning()
                .IsClosable(false),
            { Success: true } connected => InfoBar(
                    "GitHub App connected",
                    $"{connected.AppName} · {connected.InstallationLogin} · {connected.RepositoryCount} repositories")
                .Success()
                .IsClosable(false),
            _ => Empty(),
        };
    }
}
