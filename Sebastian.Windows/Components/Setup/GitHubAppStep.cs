using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.Providers;
using Sebastian.Windows.Helpers;
using Sebastian.Windows.Setup;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using AppContexts = Sebastian.Windows.Bootstrap.AppContexts;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal sealed record GitHubAppDraft(
    string AppId = "",
    string InstallationId = "",
    string PrivateKey = "",
    string PrivateKeyFileName = "",
    string WebhookSecret = "",
    string WebhookUrl = "",
    bool HasStoredPrivateKey = false,
    bool HasStoredWebhookSecret = false);

internal sealed record GitHubAppStepProps(
    GitHubAppDraft Draft,
    Action<GitHubAppDraft> SetDraft,
    PatValidation? Pat,
    Action Back,
    Action<GitHubAppValidation> Next);

internal sealed class GitHubAppStep : Component<GitHubAppStepProps>
{
    private const int MinWebhookSecretLength = 16;
    private const string AppIdField = "appId";
    private const string InstallationIdField = "installationId";
    private const string WebhookSecretField = "webhookSecret";
    private const string WebhookUrlField = "webhookUrl";

    public override Element Render()
    {
        var setup = UseContext(AppContexts.Setup);
        var validation = this.UseValidationContext();
        var window = UseWindow();
        var wide = UseBreakpoint(720);
        (var _, var windowHeight) = UseWindowSize();
        // Room for the title bar, setup padding, step caption, and the actions row.
        var pageHeight = SetupLayout.PageHeight(windowHeight);
        var draft = Props.Draft;

        (var copied, var setCopied) = UseState(false);
        (var keyError, var setKeyError) = UseState<string?>(null);
        var keyValid = UseMemo(
            () => draft.HasStoredPrivateKey || GitHubPrivateKey.IsValid(draft.PrivateKey),
            draft.HasStoredPrivateKey,
            draft.PrivateKey);
        var secretValid = string.IsNullOrWhiteSpace(draft.WebhookSecret)
            ? draft.HasStoredWebhookSecret
            : IsWebhookSecretValid(draft.WebhookSecret);
        var test = UseMutation<GitHubAppCredentials, GitHubAppValidation>(
            setup.TestAndSaveGitHubAppAsync);

        void SetDraft(GitHubAppDraft next)
        {
            Props.SetDraft(next);
            test.Reset();
        }

        var fields = BuildFields(
            draft,
            validation,
            SetDraft,
            copied,
            setCopied,
            keyError,
            () => _ = PickKeyAsync(window, draft, SetDraft, setKeyError));
        var canNext = CanContinue(draft, secretValid, keyValid, test.IsPending);
        var status = BuildStatus(test);
        var patInfo = BuildPatInfo(Props.Pat);

        var next = Button("Next", () => _ = SubmitAsync(canNext, test, draft, Props.Next))
            .AccentButton()
            .IsEnabled(canNext)
            .AutomationName("Continue to the provider step");
        var back = Button("Back", Props.Back)
            .SubtleButton()
            .AutomationName("Back to the GitHub token step");

        var actions = SetupLayout.Actions(wide, back, next);

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
            BuildFieldRow(wide, fields.AppId, fields.InstallationId).Margin(0, 24, 0, 0),
            fields.Key.Margin(0, 12, 0, 0),
            SubHeading("Webhook")
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(0, 24, 0, 0),
            BuildFieldRow(wide, fields.WebhookSecret, fields.WebhookUrl).Margin(0, 12, 0, 0),
            status.Margin(0, 16, 0, 0));

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

    private sealed record AppFields(
        Element AppId,
        Element InstallationId,
        Element Key,
        Element WebhookSecret,
        Element WebhookUrl);

    private static AppFields BuildFields(
        GitHubAppDraft draft,
        ValidationContext validation,
        Action<GitHubAppDraft> setDraft,
        bool copied,
        Action<bool> setCopied,
        string? keyError,
        Action pickKey)
    {
        var appId = FormField(
            TextBox(draft.AppId, value =>
                {
                    setDraft(draft with { AppId = value });
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
                        setDraft(draft with { InstallationId = value });
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

        var hasKey = draft.PrivateKeyFileName.Length > 0 || draft.HasStoredPrivateKey;
        var keyField = FormField(
            VStack(
                8,
                FlexRow(
                        Button(
                                hasKey
                                    ? "Replace .pem file…"
                                    : "Choose .pem file…",
                                pickKey)
                            .AutomationName(
                                hasKey
                                    ? "Replace GitHub App private key file"
                                    : "Choose GitHub App private key file"),
                        BuildKeyStatus(draft))
                    with
                {
                    Wrap = FlexWrap.Wrap,
                    AlignItems = FlexAlign.Center,
                    ColumnGap = 12,
                    RowGap = 4,
                },
                BuildKeyError(keyError)),
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
                            setDraft(draft with { WebhookSecret = value });
                            validation.MarkTouched(WebhookSecretField);
                            setCopied(false);
                        },
                        placeholderText: draft.HasStoredWebhookSecret
                            ? "Configured — leave blank to keep"
                            : $"At least {MinWebhookSecretLength} characters")
                    .PasswordRevealMode(PasswordRevealMode.Hidden)
                    .Validate(WebhookSecretField, draft.WebhookSecret,
                        Validate.Must<string>(
                            value => string.IsNullOrWhiteSpace(value)
                                ? draft.HasStoredWebhookSecret
                                : IsWebhookSecretValid(value),
                            $"Use at least {MinWebhookSecretLength} characters."))
                    .AutomationName("GitHub App webhook secret"),
                HStack(
                    8,
                    Button("Generate", () =>
                        {
                            setDraft(draft with { WebhookSecret = NewWebhookSecret() });
                            setCopied(false);
                        })
                        .SubtleButton()
                        .AutomationName("Generate webhook secret"),
                    Button(copied ? "Copied" : "Copy", () => CopyToClipboard(draft.WebhookSecret, setCopied))
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
                        setDraft(draft with { WebhookUrl = value });
                        validation.MarkTouched(WebhookUrlField);
                    },
                    placeholderText: "https://example.com/api/github/webhook")
                .Validate(WebhookUrlField, draft.WebhookUrl,
                    Validate.Must<string>(IsValidWebhookUrl, "Use an HTTPS URL. HTTP is allowed only for localhost."))
                .AutomationName("GitHub App webhook URL"),
            label: "Webhook URL",
            description: "Optional. Without it, GitHub cannot deliver events.",
            fieldName: WebhookUrlField);

        return new AppFields(appId, installationId, keyField, webhookSecret, webhookUrl);
    }

    private static Element BuildKeyStatus(GitHubAppDraft draft)
    {
        var text = draft switch
        {
            { PrivateKeyFileName.Length: > 0 } => $"Loaded {draft.PrivateKeyFileName}",
            { HasStoredPrivateKey: true } => "Configured — choose a file to replace",
            _ => string.Empty,
        };

        return text.Length == 0
            ? Empty()
            : HStack(
                6,
                TextBlock("\uE73E")
                    .FontFamily(IconFont.Family)
                    .FontSize(12)
                    .Foreground(Theme.SystemSuccess),
                Caption(text).Foreground(Theme.SecondaryText));
    }

    private static Element BuildKeyError(string? keyError)
    {
        return keyError is null
            ? Empty()
            : Caption(keyError)
                .Foreground(Theme.SystemCaution)
                .TextWrapping(TextWrapping.Wrap);
    }

    private static GridElement BuildFieldRow(bool wide, Element first, Element second)
    {
        return wide
            ? Grid(
                [GridSize.Star(), GridSize.Star()],
                [GridSize.Auto],
                [
                    first.Grid(row: 0, column: 0),
                    second.Grid(row: 0, column: 1),
                ]) with
            {
                ColumnSpacing = 16,
            }
            : Grid(
                [GridSize.Star()],
                [GridSize.Auto, GridSize.Auto],
                [
                    first.Grid(row: 0, column: 0),
                    second.Grid(row: 1, column: 0),
                ]) with
            {
                RowSpacing = 12,
            };
    }

    private static bool CanContinue(
        GitHubAppDraft draft,
        bool secretValid,
        bool keyValid,
        bool pending)
    {
        return IsPositiveInteger(draft.AppId) &&
            IsPositiveInteger(draft.InstallationId) &&
            secretValid &&
            IsValidWebhookUrl(draft.WebhookUrl) &&
            keyValid &&
            !pending;
    }

    private static Element BuildStatus(Mutation<GitHubAppCredentials, GitHubAppValidation> test)
    {
        return test switch
        {
            { IsPending: true } => HStack(
                8,
                ProgressRing().Width(16).Height(16),
                Body("Testing GitHub App…").Foreground(Theme.SecondaryText)),
            { Error: { } failure } => InfoBar(
                    "Could not verify the GitHub App",
                    failure.Message)
                .Error()
                .IsClosable(false),
            _ => Empty(),
        };
    }

    private static Element BuildPatInfo(PatValidation? pat)
    {
        return pat is null
            ? Empty()
            : InfoBar(
                    $"Connected as {pat.Login}",
                    $"{pat.RepositoryCount} repositories accessible.")
                .Success()
                .IsClosable(false)
                .Margin(0, 16, 0, 0);
    }

    private static void CopyToClipboard(string secret, Action<bool> setCopied)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(secret);
        Clipboard.SetContent(package);
        setCopied(true);
    }

    private static async Task PickKeyAsync(
        ReactorWindow? window,
        GitHubAppDraft draft,
        Action<GitHubAppDraft> setDraft,
        Action<string?> setKeyError)
    {
        if (window is null)
        {
            return;
        }

        var file = await FilePicker.PickFileAsync(window, ".pem").ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        try
        {
            var text = await FileIO.ReadTextAsync(file);
            if (!GitHubPrivateKey.IsValid(text))
            {
                setKeyError("That file does not contain a valid PEM private key.");
                return;
            }

            setKeyError(null);
            setDraft(draft with { PrivateKey = text, PrivateKeyFileName = file.Name });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            setKeyError($"Could not read the file: {exception.Message}");
        }
    }

    private static async Task SubmitAsync(
        bool canNext,
        Mutation<GitHubAppCredentials, GitHubAppValidation> test,
        GitHubAppDraft draft,
        Action<GitHubAppValidation> next)
    {
        if (!canNext || test.IsPending)
        {
            return;
        }

        var credentials = new GitHubAppCredentials(
            draft.AppId.Trim(),
            draft.InstallationId.Trim(),
            draft.PrivateKey,
            draft.WebhookSecret,
            string.IsNullOrWhiteSpace(draft.WebhookUrl)
                ? null
                : new Uri(draft.WebhookUrl.Trim(), UriKind.Absolute));

        GitHubAppValidation result;
        try
        {
            result = await test.RunAsync(credentials).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        next(result);
    }

    private static string NewWebhookSecret()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
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
            ProviderAdapters.ValidateBaseUrl(value).IsValid;
    }
}
