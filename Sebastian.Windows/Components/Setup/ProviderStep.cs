using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.Providers;
using Windows.System;
using AppContexts = Sebastian.Windows.Bootstrap.AppContexts;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal sealed record ProviderStepProps(
    Action Back,
    Action<ProviderTestResult> Next,
    double ContentWidth,
    GitHubAppValidation? App);

internal sealed class ProviderStep : Component<ProviderStepProps>
{
    public override Element Render()
    {
        var catalogService = UseContext(AppContexts.Catalog);
        var providerSetup = UseContext(AppContexts.Providers);
        var wide = UseBreakpoint(720);
        (var _, var windowHeight) = UseWindowSize();
        var pageHeight = Math.Max(320, windowHeight - 160);

        (var attempt, var setAttempt) = UseState(0);
        var catalog = UseResource(
            catalogService.GetAsync,
            [attempt]);

        (var providerId, var setProviderId) = UseState(providerSetup.SelectedProviderId ?? "");
        (var search, var setSearch) = UseState("");
        (var pickerOpen, var setPickerOpen) = UseState(false);
        (var baseUrl, var setBaseUrl) = UseState("");
        (var customEndpoint, var setCustomEndpoint) = UseState(false);
        (var adapterIndex, var setAdapterIndex) = UseState(0);
        (var confirmedHost, var setConfirmedHost) = UseState<string?>(null);
        (var envValues, var updateEnv) = UseReducer(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var pickerPresenterStyle = UseMemo(static () => CreatePickerPresenterStyle(), []);
        var (searchRef, focusSearch) = this.UseElementFocus();
        var (listRef, focusList) = this.UseElementFocus();
        var selectorRef = this.UseElementRef<Button>();
        var initializedProvider = UseRef<string?>(null);

        var test = UseMutation<ProviderSelection, ProviderTestResult>(
            providerSetup.TestAndSaveAsync);

        var catalogData = catalog switch
        {
            AsyncValue<ModelCatalog>.Data data => data.Value,
            AsyncValue<ModelCatalog>.Reloading reloading => reloading.Previous,
            _ => null,
        };
        var catalogError = catalog is AsyncValue<ModelCatalog>.Error error
            ? error.Exception
            : null;
        var provider = catalogData?.FindProvider(providerId);

        UseEffect(
            () =>
            {
                if (provider is null || initializedProvider.Current == provider.Id)
                {
                    return;
                }

                var stored = providerSetup.GetStoredState(provider);
                var inferred = ProviderAdapters.Infer(provider.Npm) ??
                    ProviderAdapter.OpenAiCompatible;
                var adapter = ProviderAdapters.ParseApi(stored.Api) ?? inferred;

                setBaseUrl(stored.BaseUrl ?? "");
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

        UseEffect(
            () =>
            {
                if (pickerOpen)
                {
                    focusSearch();
                }
            },
            pickerOpen);

        var query = search.Trim();
        CatalogProvider[] filtered = catalogData is null
            ? []
            : [.. catalogData.Providers
                .Where(
                    candidate =>
                        query.Length == 0 ||
                        candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        candidate.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)];

        ProviderField[] fields = provider is null
            ? []
            : [.. ProviderFields.Build(provider)];
        var primary = fields.FirstOrDefault(field => field.IsPrimaryKey);
        var apiKey = primary is not null &&
            envValues.TryGetValue(primary.Name, out var candidateKey)
            ? candidateKey
            : null;
        var stored = provider is null ? null : providerSetup.GetStoredState(provider);

        var inferredAdapter = provider is null
            ? ProviderAdapter.OpenAiCompatible
            : ProviderAdapters.Infer(provider.Npm) ?? ProviderAdapter.OpenAiCompatible;
        var effectiveAdapter = customEndpoint
            ? ProviderAdapters.FromIndex(adapterIndex)
            : inferredAdapter;
        var defaultBaseUrl = provider is null
            ? null
            : ProviderAdapters.ResolveBaseUrl(provider, envValues);

        var effectiveBaseUrl = (provider, effectiveAdapter, customEndpoint) switch
        {
            (null, _, _) => string.Empty,
            (_, ProviderAdapter.Azure, false) => defaultBaseUrl ?? string.Empty,
            _ => string.IsNullOrWhiteSpace(baseUrl)
                ? defaultBaseUrl ?? string.Empty
                : baseUrl,
        };

        var resourceError = provider is not null &&
            effectiveAdapter == ProviderAdapter.Azure &&
            !customEndpoint
            ? ProviderAdapters.ValidateAzureResourceName(
                ProviderAdapters.GetAzureResource(envValues))
            : null;
        var baseValidation = resourceError is null
            ? ProviderAdapters.ValidateBaseUrl(effectiveBaseUrl)
            : new BaseUrlValidation(false, string.Empty, resourceError);

        var destinationHost = Uri.TryCreate(
            effectiveBaseUrl,
            UriKind.Absolute,
            out var destinationUri)
            ? destinationUri.Host
            : null;
        var needsConfirmation = provider is not null &&
            destinationHost is not null &&
            ProviderAdapters.NeedsHostConfirmation(effectiveBaseUrl, provider, envValues);
        var hostConfirmed = destinationHost is not null &&
            string.Equals(confirmedHost, destinationHost, StringComparison.OrdinalIgnoreCase);

        var hasKey = !string.IsNullOrWhiteSpace(apiKey) || stored?.HasApiKey == true;
        var canTest = provider is not null &&
            primary is not null &&
            baseValidation.IsValid &&
            hasKey &&
            !test.IsPending &&
            (!needsConfirmation || hostConfirmed);

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
                var inferred = ProviderAdapters.Infer(provider.Npm) ??
                    ProviderAdapter.OpenAiCompatible;
                setAdapterIndex(ProviderAdapters.ToIndex(inferred));
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

        [SuppressMessage(
            "Design",
            "CA1031",
            Justification = "Failures are surfaced through test.Error; staying on the page is intentional.")]
        async Task SubmitAsync()
        {
            if (!canTest || test.IsPending || provider is null || primary is null)
            {
                return;
            }

            var selection = new ProviderSelection(
                provider,
                effectiveAdapter,
                baseValidation.Normalized,
                customEndpoint,
                new ProviderCredentials(
                    apiKey,
                    new Dictionary<string, string>(
                        envValues,
                        StringComparer.OrdinalIgnoreCase)));

            ProviderTestResult result;
            try
            {
                result = await test.RunAsync(selection).ConfigureAwait(true);
            }
            catch (Exception)
            {
                return;
            }

            if (result.Success)
            {
                Props.Next(result);
            }
        }

        Element EnvField(ProviderField field)
        {
            var value = envValues.TryGetValue(field.Name, out var current) ? current : "";
            var configured = field.IsPrimaryKey && stored?.HasApiKey == true;

            if (field.IsSecret)
            {
                var box = PasswordBox(
                        value,
                        newValue => UpdateEnvValue(field.Name, newValue),
                        placeholderText: configured && string.IsNullOrEmpty(value)
                            ? "Configured — leave blank to keep"
                            : "")
                    .PasswordRevealMode(PasswordRevealMode.Hidden)
                    .IsEnabled(!test.IsPending)
                    .AutomationName(field.Label);

                return FormField(
                        box,
                        label: field.IsPrimaryKey ? "API key" : field.Label,
                        required: field.IsPrimaryKey,
                        description: field.Aliases.Count > 1
                            ? $"Use any of: {string.Join(", ", field.Aliases)}."
                            : $"Environment variable: {field.Name}")
                    .Margin(0, 12, 0, 0);
            }

            var textBox = TextBox(
                    value,
                    newValue => UpdateEnvValue(field.Name, newValue),
                    placeholderText: field.Name == "AZURE_RESOURCE_NAME"
                        ? "my-resource"
                        : field.Name)
                .IsEnabled(!test.IsPending)
                .AutomationName(field.Label);

            return FormField(
                    textBox,
                    label: field.Label,
                    description: $"Environment variable: {field.Name}")
                .Margin(0, 12, 0, 0);
        }

        var testStatus = test switch
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
        var keyHint = stored?.HasApiKey == true && string.IsNullOrWhiteSpace(apiKey)
            ? Body("An API key is already configured for this provider. Leave blank to keep it, or paste a new one.")
                .Foreground(Theme.SecondaryText)
            : Empty();

        var header = VStack(
            0,
            Title("Choose a provider")
                .TextWrapping(TextWrapping.Wrap)
                .HeadingLevel(AutomationHeadingLevel.Level1),
            Body("Pick a model provider, then verify its API key.")
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.Wrap)
                .Margin(0, 8, 0, 0));

        var appInfo = Props.App switch
        {
            { Success: true, RepositoryCount: 0 } app => InfoBar(
                    "GitHub App connected",
                    $"The installation covers no repositories. Install it: {app.InstallUrl}")
                .Warning()
                .IsClosable(false),
            { Success: true } app => InfoBar(
                    "GitHub App connected",
                    $"{app.AppName} · {app.InstallationLogin} · {app.RepositoryCount} repositories")
                .Success()
                .IsClosable(false),
            _ => Empty(),
        };

        Element catalogSection;
        if (catalogData is null)
        {
            catalogSection = catalogError is not null
                ? VStack(
                    16,
                    InfoBar("Could not load the provider catalog", catalogError.Message)
                        .Error()
                        .IsClosable(false),
                    Button("Retry", () => setAttempt(attempt + 1))
                        .AccentButton()
                        .AutomationName("Retry provider catalog"))
                : HStack(
                    8,
                    ProgressRing().Width(16).Height(16),
                    Body("Loading provider catalog…")
                        .Foreground(Theme.SecondaryText));
        }
        else
        {
            var selectedIndex = Array.FindIndex(filtered, item => item.Id == providerId);
            var providerList = ListView(
                    selectedIndex < 0
                        ? Optional<int>.Unset
                        : Optional<int>.Of(selectedIndex),
                    index =>
                    {
                        if (index >= 0 && index < filtered.Length)
                        {
                            SelectProvider(filtered[index]);
                        }
                    },
                    [.. filtered
                        .Select(item => ProviderRow(item).WithKey(item.Id))])
                .SelectionMode(ListViewSelectionMode.Single)
                .ItemClick(
                    index =>
                    {
                        if (index >= 0 && index < filtered.Length)
                        {
                            SelectProvider(filtered[index]);
                            setPickerOpen(false);
                            setSearch("");
                            selectorRef.Current?.Flyout?.Hide();
                        }
                    })
                .Set(list => list.IsItemClickEnabled = true)
                .Ref(listRef)
                .IsEnabled(!test.IsPending)
                .AutomationName("Provider list");

            var searchBox = TextBox(
                    search,
                    setSearch,
                    placeholderText: "Search providers")
                .SearchInput()
                .IsEnabled(!test.IsPending)
                .AutomationName("Search providers")
                .Ref(searchRef)
                .OnKeyDown(
                    (_, args) =>
                    {
                        if (args.Key == VirtualKey.Down)
                        {
                            focusList();
                            args.Handled = true;
                            return;
                        }

                        if (args.Key == VirtualKey.Enter && filtered.Length > 0)
                        {
                            SelectProvider(filtered[0]);
                            setPickerOpen(false);
                            setSearch("");
                            selectorRef.Current?.Flyout?.Hide();
                            args.Handled = true;
                        }
                    });

            var triggerContent = Grid(
                [GridSize.Star(), GridSize.Auto],
                [GridSize.Auto],
                [
                    BodyStrong(provider?.Name ?? "Select a provider")
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(row: 0, column: 0),
                    TextBlock("\uE70D")
                        .FontFamily("Segoe Fluent Icons")
                        .FontSize(12)
                        .Foreground(Theme.SecondaryText)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(row: 0, column: 1),
                ]) with
            {
                ColumnSpacing = 8,
            };

            var providerSelector = Button(triggerContent)
                .HAlign(HorizontalAlignment.Stretch)
                .Padding(12, 8)
                .Background(Theme.ControlFill)
                .BorderBrush(Theme.ControlStroke)
                .BorderThickness(1)
                .CornerRadius(4)
                .IsEnabled(!test.IsPending)
                .AutomationName(provider is null
                    ? "Select provider"
                    : $"Selected provider: {provider.Name}")
                .Ref(selectorRef)
                .Set(button => button.HorizontalContentAlignment = HorizontalAlignment.Stretch);

            Element pickerBody = filtered.Length == 0
                ? VStack(
                    8,
                    Body("No providers match.")
                        .Foreground(Theme.SecondaryText),
                    Button("Clear search", () => setSearch(""))
                        .SubtleButton()
                        .HAlign(HorizontalAlignment.Left))
                : providerList;

            var pickerContent = Grid(
                    [GridSize.Star()],
                    [GridSize.Star(), GridSize.Auto],
                    [
                        pickerBody.Grid(row: 0, column: 0),
                        searchBox
                            .Margin(0, 12, 0, 0)
                            .Grid(row: 1, column: 0),
                    ])
                .Padding(12)
                .Width(Math.Max(280, Props.ContentWidth))
                .Height(wide ? 420 : 300);

            var providerPicker = (Flyout(providerSelector, pickerContent) with
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                OnOpened = () => setPickerOpen(true),
                OnClosed = () =>
                {
                    setPickerOpen(false);
                    setSearch("");
                },
            })
                .Set(
                    flyout =>
                    {
                        flyout.AllowFocusOnInteraction = true;
                        flyout.FlyoutPresenterStyle = pickerPresenterStyle;
                    });

            var catalogWarning = catalogData.Warning is null
                ? Empty()
                : InfoBar("Using cached catalog", catalogData.Warning)
                    .Warning()
                    .IsClosable(false);

            var details = provider is null ? Empty() : ProviderDetails();

            Element ProviderDetails()
            {
                if (provider is null)
                {
                    return Empty();
                }

                var adapterWarning = ProviderAdapters.Infer(provider.Npm) is null
                    ? InfoBar(
                            "No adapter is mapped",
                            $"No adapter is mapped for `{provider.Npm ?? "(none)"}`. Sebastian will treat this endpoint as OpenAI-compatible. Confirm the base URL before testing.")
                        .Warning()
                        .IsClosable(false)
                    : Empty();

                var customToggle = ToggleSwitch(
                        customEndpoint,
                        SetCustom,
                        header: "Custom endpoint")
                    .IsEnabled(!test.IsPending)
                    .AutomationName("Custom endpoint");

                Element adapterSelector = customEndpoint
                    ? VStack(
                        6,
                        Body("API type").Foreground(Theme.SecondaryText),
                        RadioButtons(
                            ["OpenAI-compatible", "Anthropic", "Azure"],
                            adapterIndex,
                            SetAdapter)
                            .IsEnabled(!test.IsPending)
                            .AutomationName("Provider API type"))
                    : Caption($"API type: {ProviderAdapters.Label(inferredAdapter)}")
                        .Foreground(Theme.SecondaryText);

                var derivedBaseUrl = effectiveAdapter == ProviderAdapter.Azure &&
                    !customEndpoint;

                var baseUrlSection = derivedBaseUrl
                    ? VStack(
                        4,
                        Caption(
                                string.IsNullOrWhiteSpace(effectiveBaseUrl)
                                    ? "Enter the Azure resource name to derive the endpoint."
                                    : $"Endpoint: {effectiveBaseUrl}")
                            .Foreground(Theme.SecondaryText)
                            .TextWrapping(TextWrapping.Wrap),
                        baseValidation.Error is null
                            ? Empty()
                            : Caption(baseValidation.Error)
                                .Foreground(Theme.SystemCaution)
                                .TextWrapping(TextWrapping.Wrap))
                    : VStack(
                        0,
                        FormField(
                            TextBox(
                                    baseUrl,
                                    UpdateBaseUrl,
                                    placeholderText: defaultBaseUrl ?? "https://…")
                                .IsEnabled(!test.IsPending)
                                .AutomationName("Provider base URL"),
                            label: "Base URL",
                            required: defaultBaseUrl is null,
                            description: defaultBaseUrl is null
                                ? "The provider endpoint."
                                : $"Default: {defaultBaseUrl}"),
                        baseValidation.Error is null
                            ? Empty()
                            : Caption(baseValidation.Error)
                                .Foreground(Theme.SystemCaution)
                                .TextWrapping(TextWrapping.Wrap)
                                .Margin(0, 4, 0, 0));

                var envSection = fields.Length == 0
                    ? Empty()
                    : VStack(0, [.. fields.Select(EnvField)]);

                var hostSection = destinationHost is null
                    ? Empty()
                    : VStack(
                        8,
                        Caption($"The API key will be sent to {destinationHost}.")
                            .Foreground(Theme.SecondaryText)
                            .TextWrapping(TextWrapping.Wrap),
                        needsConfirmation
                            ? CheckBox(
                                hostConfirmed,
                                value => setConfirmedHost(value ? destinationHost : null),
                                label: $"I understand the key will be sent to {destinationHost}.")
                            : Empty());

                return VStack(
                    0,
                    adapterWarning.Margin(0, 16, 0, 0),
                    customToggle.Margin(0, 16, 0, 0),
                    adapterSelector.Margin(0, 12, 0, 0),
                    baseUrlSection.Margin(0, 12, 0, 0),
                    envSection,
                    keyHint.Margin(0, 8, 0, 0),
                    hostSection.Margin(0, 16, 0, 0));
            }

            catalogSection = VStack(
                0,
                catalogWarning.Margin(0, 0, 0, 12),
                providerPicker,
                details.Margin(0, 20, 0, 0));
        }

        var nextButton = Button("Next", () => _ = SubmitAsync())
            .AccentButton()
            .IsEnabled(canTest)
            .AutomationName("Test provider and continue");
        var backButton = Button("Back", Props.Back)
            .SubtleButton()
            .AutomationName("Back to the GitHub App step");

        Element actions = wide
            ? Grid(
                [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                [GridSize.Auto],
                [
                    backButton.Grid(row: 0, column: 0),
                    Empty().Grid(row: 0, column: 1),
                    nextButton.Grid(row: 0, column: 2),
                ])
            : VStack(
                8,
                nextButton.HAlign(HorizontalAlignment.Right),
                backButton.HAlign(HorizontalAlignment.Left));

        return Grid(
            [GridSize.Star()],
            [GridSize.Star(), GridSize.Auto],
            [
                ScrollView(
                        VStack(
                            0,
                            header,
                            appInfo.Margin(0, 16, 0, 0),
                            catalogSection.Margin(0, 24, 0, 0)))
                    .Grid(row: 0, column: 0),
                VStack(
                    0,
                    testStatus,
                    actions.Margin(0, 12, 0, 0))
                    .Grid(row: 1, column: 0),
            ])
            .Height(pageHeight);
    }

    private static TextBlockElement ProviderRow(CatalogProvider provider)
    {
        return Body(provider.Name).Padding(8, 8);
    }

    private static Style CreatePickerPresenterStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 10000d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        return style;
    }
}
