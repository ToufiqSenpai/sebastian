using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.Providers;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal sealed record ProviderDetailsState(
    CatalogProvider? Provider,
    ProviderField[] Fields,
    IReadOnlyDictionary<string, string> Env,
    ProviderStoredState? Stored,
    ProviderEndpoint Endpoint,
    BaseUrlValidation Validation,
    string? DestinationHost,
    bool NeedsConfirmation,
    bool HostConfirmed,
    string? ApiKey,
    string BaseUrl,
    ProviderAdapter InferredAdapter,
    int AdapterIndex,
    bool CustomEndpoint,
    bool Busy);

internal sealed record ProviderDetailsActions(
    Action<string> SetBaseUrl,
    Action<string, string> SetEnvValue,
    Action<bool> SetCustomEndpoint,
    Action<int> SetAdapter,
    Action<string?> SetConfirmedHost);

internal sealed record ProviderDetailsProps(
    ProviderDetailsState State,
    ProviderDetailsActions Actions);

internal sealed class ProviderDetails : Component<ProviderDetailsProps>
{
    public override Element Render()
    {
        var state = Props.State;
        var actions = Props.Actions;

        return state.Provider is null
            ? Empty()
            : VStack(
            0,
            BuildAdapterWarning(state.Provider).Margin(0, 16, 0, 0),
            BuildCustomEndpointToggle(state, actions).Margin(0, 16, 0, 0),
            BuildAdapterSelector(state, actions).Margin(0, 12, 0, 0),
            BuildBaseUrlSection(state, actions).Margin(0, 12, 0, 0),
            BuildEnvSection(state, actions),
            BuildHostSection(state, actions).Margin(0, 16, 0, 0));
    }

    private static Element BuildAdapterWarning(CatalogProvider provider)
    {
        return ProviderAdapters.Infer(provider.Npm) is null
            ? InfoBar(
                    "No adapter is mapped",
                    $"No adapter is mapped for `{provider.Npm ?? "(none)"}`. Sebastian will treat this endpoint as OpenAI-compatible. Confirm the base URL before testing.")
                .Warning()
                .IsClosable(false)
            : Empty();
    }

    private static ToggleSwitchElement BuildCustomEndpointToggle(
        ProviderDetailsState state,
        ProviderDetailsActions actions)
    {
        return ToggleSwitch(
                state.CustomEndpoint,
                actions.SetCustomEndpoint,
                header: "Custom endpoint")
            .IsEnabled(!state.Busy)
            .AutomationName("Custom endpoint");
    }

    private static Element BuildAdapterSelector(
        ProviderDetailsState state,
        ProviderDetailsActions actions)
    {
        return !state.CustomEndpoint
            ? Caption($"API type: {ProviderAdapters.Label(state.InferredAdapter)}")
                .Foreground(Theme.SecondaryText)
            : VStack(
                6,
                Body("API type").Foreground(Theme.SecondaryText),
                RadioButtons(
                    ["OpenAI-compatible", "Anthropic", "Azure"],
                    state.AdapterIndex,
                    actions.SetAdapter)
                    .IsEnabled(!state.Busy)
                    .AutomationName("Provider API type"));
    }

    private static StackElement BuildBaseUrlSection(
        ProviderDetailsState state,
        ProviderDetailsActions actions)
    {
        return state.Endpoint.DerivedBaseUrl
            ? VStack(
                4,
                string.IsNullOrWhiteSpace(state.Endpoint.EffectiveBaseUrl)
                    ? Empty()
                    : Caption($"Endpoint: {state.Endpoint.EffectiveBaseUrl}")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap),
                BuildValidationError(state.Validation))
            : VStack(
                0,
                FormField(
                    TextBox(
                            state.BaseUrl,
                            actions.SetBaseUrl,
                            placeholderText: state.Endpoint.DefaultBaseText ?? "https://…")
                        .IsEnabled(!state.Busy)
                        .AutomationName("Provider base URL"),
                    label: "Base URL",
                    required: state.Endpoint.DefaultBaseText is null),
                BuildValidationError(state.Validation).Margin(0, 4, 0, 0));
    }

    private static Element BuildValidationError(BaseUrlValidation validation)
    {
        return validation.Error is null
            ? Empty()
            : Caption(validation.Error)
                .Foreground(Theme.SystemCaution)
                .TextWrapping(TextWrapping.Wrap);
    }

    private static Element BuildEnvSection(
        ProviderDetailsState state,
        ProviderDetailsActions actions)
    {
        return state.Fields.Length == 0
            ? Empty()
            : VStack(0, [.. state.Fields.Select(field => BuildEnvField(field, state, actions))]);
    }

    private static FormFieldElement BuildEnvField(
        ProviderField field,
        ProviderDetailsState state,
        ProviderDetailsActions actions)
    {
        var value = state.Env.TryGetValue(field.Name, out var current) ? current : "";
        var configured = field.IsPrimaryKey && state.Stored?.HasApiKey is true;

        if (field.IsSecret)
        {
            var box = PasswordBox(
                    value,
                    newValue => actions.SetEnvValue(field.Name, newValue),
                    placeholderText: configured && string.IsNullOrEmpty(value)
                        ? "Configured — leave blank to keep"
                        : "")
                .PasswordRevealMode(PasswordRevealMode.Hidden)
                .IsEnabled(!state.Busy)
                .AutomationName(field.Label);

            return FormField(
                    box,
                    label: field.Label,
                    required: field.IsPrimaryKey)
                .Margin(0, 12, 0, 0);
        }

        var textBox = TextBox(
                value,
                newValue => actions.SetEnvValue(field.Name, newValue),
                placeholderText: field.Name == "AZURE_RESOURCE_NAME"
                    ? "my-resource"
                    : field.Name)
            .IsEnabled(!state.Busy)
            .AutomationName(field.Label);

        return FormField(
                textBox,
                label: field.Label)
            .Margin(0, 12, 0, 0);
    }

    private static Element BuildHostSection(
        ProviderDetailsState state,
        ProviderDetailsActions actions)
    {
        if (state.DestinationHost is null)
        {
            return Empty();
        }

        var confirmation = BuildHostConfirmation(state, actions);

        return VStack(
            8,
            Caption($"The API key will be sent to {state.DestinationHost}.")
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.Wrap),
            confirmation);
    }

    private static Element BuildHostConfirmation(
        ProviderDetailsState state,
        ProviderDetailsActions actions)
    {
        return state.NeedsConfirmation
            ? CheckBox(
                state.HostConfirmed,
                value => actions.SetConfirmedHost(value ? state.DestinationHost : null),
                label: $"I understand the key will be sent to {state.DestinationHost}.")
            : Empty();
    }
}
