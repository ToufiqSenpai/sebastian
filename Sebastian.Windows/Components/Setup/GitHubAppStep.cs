using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Sebastian.Windows.Setup;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal sealed record GitHubAppDraft(
    string AppId = "",
    string InstallationId = "",
    string PrivateKey = "",
    string PrivateKeyFileName = "",
    string WebhookSecret = "",
    string WebhookUrl = "");

internal sealed record GitHubAppStepProps(
    GitHubAppDraft Draft,
    Action<GitHubAppDraft> SetDraft,
    PatValidation? Pat,
    Action Back,
    Action Next);

internal sealed class GitHubAppStep : Component<GitHubAppStepProps>
{
    private const int MinWebhookSecretLength = 16;
    private const string AppIdField = "appId";
    private const string InstallationIdField = "installationId";
    private const string WebhookSecretField = "webhookSecret";
    private const string WebhookUrlField = "webhookUrl";

    public override Element Render()
    {
        var validation = this.UseValidationContext();
        var window = UseWindow();
        var wide = UseBreakpoint(720);
        (var _, var windowHeight) = UseWindowSize();
        // Room for the title bar, setup padding, step caption, and the actions row.
        var pageHeight = Math.Max(320, windowHeight - 160);
        var draft = Props.Draft;

        (var copied, var setCopied) = UseState(false);
        (var keyError, var setKeyError) = UseState<string?>(null);
        var keyValid = UseMemo(() => IsValidPrivateKey(draft.PrivateKey), draft.PrivateKey);

        void Update(Func<GitHubAppDraft, GitHubAppDraft> change)
        {
            Props.SetDraft(change(draft));
        }

        void GenerateSecret()
        {
            Update(value => value with { WebhookSecret = NewWebhookSecret() });
            setCopied(false);
        }

        void CopySecret()
        {
            if (string.IsNullOrEmpty(draft.WebhookSecret))
            {
                return;
            }

            var package = new DataPackage();
            package.SetText(draft.WebhookSecret);
            Clipboard.SetContent(package);
            setCopied(true);
        }

        [SuppressMessage("Design", "CA1031", Justification = "A failed file read is shown inline; the step stays usable.")]
        async Task PickKeyAsync()
        {
            if (window is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".pem");
            var hwnd = Win32Interop.GetWindowFromWindowId(window.NativeWindow.AppWindow.Id);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            try
            {
                var text = await FileIO.ReadTextAsync(file);
                if (!IsValidPrivateKey(text))
                {
                    setKeyError("That file does not contain a valid PEM private key.");
                    return;
                }

                setKeyError(null);
                Update(value => value with { PrivateKey = text, PrivateKeyFileName = file.Name });
            }
            catch (Exception exception)
            {
                setKeyError($"Could not read the file: {exception.Message}");
            }
        }

        var appId = FormField(
            TextBox(draft.AppId, value =>
                {
                    Update(current => current with { AppId = value });
                    validation.MarkTouched(AppIdField);
                }, placeholderText: "123456")
                .NumericInput()
                .MaxLength(20)
                .Validate(AppIdField, draft.AppId,
                    Validate.Must<string>(IsPositiveInteger, "Enter the numeric App ID."))
                .AutomationName("GitHub App ID"),
            label: "App ID",
            required: true,
            description: "Found on the GitHub App settings page.",
            fieldName: AppIdField);

        var installationId = FormField(
            TextBox(
                    draft.InstallationId,
                    value =>
                    {
                        Update(current => current with { InstallationId = value });
                        validation.MarkTouched(InstallationIdField);
                    },
                    placeholderText: "12345678")
                .NumericInput()
                .MaxLength(20)
                .Validate(InstallationIdField, draft.InstallationId,
                    Validate.Must<string>(IsPositiveInteger, "Enter the numeric installation ID."))
                .AutomationName("GitHub App installation ID"),
            label: "Installation ID",
            required: true,
            description: "The installation that grants access to your repositories.",
            fieldName: InstallationIdField);

        var keyStatus = draft.PrivateKeyFileName.Length == 0
            ? Empty()
            : HStack(
                6,
                TextBlock("\uE73E")
                    .FontFamily("Segoe Fluent Icons")
                    .FontSize(12)
                    .Foreground(Theme.SystemSuccess),
                Caption($"Loaded {draft.PrivateKeyFileName}").Foreground(Theme.SecondaryText));

        var keyField = FormField(
            VStack(
                8,
                FlexRow(
                        Button(
                                draft.PrivateKeyFileName.Length == 0
                                    ? "Choose .pem file…"
                                    : "Replace .pem file…",
                                () => _ = PickKeyAsync())
                            .AutomationName(
                                draft.PrivateKeyFileName.Length == 0
                                    ? "Choose GitHub App private key file"
                                    : "Replace GitHub App private key file"),
                        keyStatus)
                    with
                {
                    Wrap = FlexWrap.Wrap,
                    AlignItems = FlexAlign.Center,
                    ColumnGap = 12,
                    RowGap = 4,
                },
                keyError is null
                    ? Empty()
                    : Caption(keyError)
                        .Foreground(Theme.SystemCaution)
                        .TextWrapping(TextWrapping.Wrap)),
            label: "Private key",
            required: true,
            description: "Open the .pem file downloaded from GitHub. Its path is not stored.");

        var webhookSecret = FormField(
            VStack(
                8,
                PasswordBox(
                        draft.WebhookSecret,
                        value =>
                        {
                            Update(current => current with { WebhookSecret = value });
                            validation.MarkTouched(WebhookSecretField);
                            setCopied(false);
                        },
                        placeholderText: $"At least {MinWebhookSecretLength} characters")
                    .PasswordRevealMode(PasswordRevealMode.Hidden)
                    .Validate(WebhookSecretField, draft.WebhookSecret,
                        Validate.Must<string>(
                            IsWebhookSecretValid,
                            $"Use at least {MinWebhookSecretLength} characters."))
                    .AutomationName("GitHub App webhook secret"),
                HStack(
                    8,
                    Button("Generate", GenerateSecret)
                        .SubtleButton()
                        .AutomationName("Generate webhook secret"),
                    Button(copied ? "Copied" : "Copy", CopySecret)
                        .SubtleButton()
                        .IsEnabled(!string.IsNullOrEmpty(draft.WebhookSecret))
                        .AutomationName("Copy webhook secret"))),
            label: "Webhook secret",
            required: true,
            description: "GitHub signs each delivery with this secret.",
            fieldName: WebhookSecretField);

        var webhookUrl = FormField(
            TextBox(
                    draft.WebhookUrl,
                    value =>
                    {
                        Update(current => current with { WebhookUrl = value });
                        validation.MarkTouched(WebhookUrlField);
                    },
                    placeholderText: "https://example.com/api/github/webhook")
                .Validate(WebhookUrlField, draft.WebhookUrl,
                    Validate.Must<string>(IsValidWebhookUrl, "Use an HTTPS URL. HTTP is allowed only for localhost."))
                .AutomationName("GitHub App webhook URL"),
            label: "Webhook URL",
            description: "Optional. Without it, GitHub cannot deliver events.",
            fieldName: WebhookUrlField);

        Element idFields = wide
            ? Grid(
                [GridSize.Star(), GridSize.Star()],
                [GridSize.Auto],
                [
                    appId.Grid(row: 0, column: 0),
                    installationId.Grid(row: 0, column: 1),
                ]) with
            {
                ColumnSpacing = 16,
            }
            : VStack(12, appId, installationId);

        Element webhookFields = wide
            ? Grid(
                [GridSize.Star(), GridSize.Star()],
                [GridSize.Auto],
                [
                    webhookSecret.Grid(row: 0, column: 0),
                    webhookUrl.Grid(row: 0, column: 1),
                ]) with
            {
                ColumnSpacing = 16,
            }
            : VStack(12, webhookSecret, webhookUrl);

        var canNext =
            IsPositiveInteger(draft.AppId) &&
            IsPositiveInteger(draft.InstallationId) &&
            IsWebhookSecretValid(draft.WebhookSecret) &&
            IsValidWebhookUrl(draft.WebhookUrl) &&
            keyValid;

        // shortcut: local validation only; the GitHub App test and key storage land before this step can finish.
        var next = Button("Next", Props.Next)
            .AccentButton()
            .IsEnabled(canNext)
            .AutomationName("Continue to the provider step");
        var back = Button("Back", Props.Back)
            .SubtleButton()
            .AutomationName("Back to the GitHub token step");

        Element actions = wide
            ? Grid(
                [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                [GridSize.Auto],
                [
                    back.Grid(row: 0, column: 0),
                    Empty().Grid(row: 0, column: 1),
                    next.Grid(row: 0, column: 2),
                ])
            : VStack(
                8,
                next.HAlign(HorizontalAlignment.Right),
                back.HAlign(HorizontalAlignment.Left));

        var patInfo = Props.Pat is null
            ? Empty()
            : InfoBar(
                    $"Connected as {Props.Pat.Login}",
                    $"{Props.Pat.RepositoryCount} repositories accessible.")
                .Success()
                .IsClosable(false)
                .Margin(0, 16, 0, 0);

        var linkRow = FlexRow(
                HyperlinkButton(
                        "Open GitHub Apps settings",
                        new Uri("https://github.com/settings/apps"))
                    .TextLink()
                    .Padding(0),
                Caption("Create one there if you do not have an app yet.")
                    .Foreground(Theme.SecondaryText))
            with
        {
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlign.Center,
            ColumnGap = 12,
            RowGap = 4,
        };

        var form = VStack(
            0,
            Title("Connect your GitHub App")
                .TextWrapping(TextWrapping.Wrap)
                .HeadingLevel(AutomationHeadingLevel.Level1),
            Body("Create a GitHub App, install it on your account, then copy its details here.")
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.Wrap)
                .Margin(0, 8, 0, 0),
            linkRow.Margin(0, 8, 0, 0),
            patInfo,
            idFields.Margin(0, 24, 0, 0),
            keyField.Margin(0, 12, 0, 0),
            SubHeading("Webhook")
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(0, 24, 0, 0),
            webhookFields.Margin(0, 12, 0, 0));

        // preview.16 requires an explicit provide; newer Reactor versions auto-provide.
        return Grid(
            [GridSize.Star()],
            [GridSize.Star(), GridSize.Auto],
            [
                ScrollView(form).Grid(row: 0, column: 0),
                actions.Margin(0, 24, 0, 0).Grid(row: 1, column: 0),
            ])
            .Height(pageHeight)
            .Provide(ValidationContexts.Current, validation);
    }

    private static string NewWebhookSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsPositiveInteger(string value)
    {
        return long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0;
    }

    private static bool IsWebhookSecretValid(string value)
    {
        return value.Length >= MinWebhookSecretLength;
    }

    private static bool IsValidWebhookUrl(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps ||
                    (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)));
    }

    private static bool IsValidPrivateKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
