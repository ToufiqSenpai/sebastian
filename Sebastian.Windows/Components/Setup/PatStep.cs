using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AppContexts = Sebastian.Windows.Bootstrap.AppContexts;
using Sebastian.Windows.Setup;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal sealed record PatStepProps(
    Action Back,
    Action<PatValidation?> Next);

internal sealed class PatStep : Component<PatStepProps>
{
    public override Element Render()
    {
        var setup = UseContext(AppContexts.Setup);
        (var token, var setToken) = UseState("");
        var configured = setup.IsPatConfigured;
        var wide = UseBreakpoint(720);

        var test = UseMutation<string, PatValidation>(
            (value, cancellationToken) => setup.TestPatAsync(value, cancellationToken));

        void UpdateToken(string value)
        {
            setToken(value);
            test.Reset();
        }

        var canNext = !test.IsPending &&
            (!string.IsNullOrWhiteSpace(token) || configured);

        var tokenField = FormField(
            PasswordBox(
                    token,
                    UpdateToken,
                    placeholderText: configured
                        ? "Configured — leave blank to keep"
                        : "ghp_…")
                .PasswordRevealMode(PasswordRevealMode.Hidden)
                .IsEnabled(!test.IsPending)
                .AutomationName("GitHub personal access token"),
            label: "Personal access token",
            required: true,
            description: "Classic token with the repo scope.");

        var linkRow = FlexRow(
                HyperlinkButton(
                        "Create a classic token",
                        new Uri("https://github.com/settings/tokens/new?scopes=repo&description=Sebastian"))
                    .TextLink()
                    .Padding(0),
                Caption("Fine-grained tokens are not supported yet.")
                    .Foreground(Theme.SecondaryText))
            with
        {
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlign.Center,
            ColumnGap = 12,
            RowGap = 4,
        };

        var status = test switch
        {
            { IsPending: true } => HStack(
                8,
                ProgressRing().Width(16).Height(16),
                Body("Testing…").Foreground(Theme.SecondaryText)),
            { Error: not null } => InfoBar("Could not verify token", test.Error.Message).Error().IsClosable(false),
            _ when configured && string.IsNullOrWhiteSpace(token) => Body(
                    "A token is already configured. Leave blank to keep it, or paste a new one.")
                .Foreground(Theme.SecondaryText),
            _ => Empty(),
        };

        [SuppressMessage("Design", "CA1031", Justification = "Failures are surfaced through test.Error; staying on the page is intentional.")]
        async Task SubmitAsync()
        {
            if (test.IsPending)
            {
                return;
            }

            if (configured && string.IsNullOrWhiteSpace(token))
            {
                Props.Next(null);
                return;
            }

            PatValidation validation;
            try
            {
                validation = await test.RunAsync(token).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // test.Error holds the user-facing message; the InfoBar shows it and we stay.
                return;
            }

            Props.Next(validation);
        }

        var nextButton = Button("Next", () => _ = SubmitAsync())
            .AccentButton()
            .IsEnabled(canNext);

        Element actions = wide
            ? Grid(
                [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                [GridSize.Auto],
                [
                    Button("Back", Props.Back).SubtleButton().Grid(row: 0, column: 0),
                    Empty().Grid(row: 0, column: 1),
                    nextButton.Grid(row: 0, column: 2),
                ])
            : VStack(
                8,
                nextButton.HAlign(HorizontalAlignment.Right),
                Button("Back", Props.Back).SubtleButton().HAlign(HorizontalAlignment.Left));

        return VStack(
                0,
                Title("Connect GitHub")
                    .TextWrapping(TextWrapping.Wrap),
                Body("Paste a classic personal access token with the repo scope.")
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.Wrap)
                    .Margin(0, 8, 0, 0),
                tokenField.Margin(0, 24, 0, 0),
                linkRow.Margin(0, 8, 0, 0),
                status.Margin(0, 16, 0, 0),
                actions.Margin(0, 24, 0, 0));
    }
}
