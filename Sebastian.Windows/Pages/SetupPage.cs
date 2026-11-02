using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.Providers;
using Sebastian.Windows.Components.Setup;
using Sebastian.Windows.Setup;
using AppContexts = Sebastian.Windows.Bootstrap.AppContexts;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Pages;

internal sealed class SetupPage : Component
{
    public override Element Render()
    {
        var setup = UseContext(AppContexts.Setup);
        (var step, var setStep) = UseState(GetInitialStep(setup));
        (var verifiedPat, var setVerifiedPat) = UseState<PatValidation?>(null);
        (var verifiedApp, var setVerifiedApp) = UseState<GitHubAppValidation?>(null);
        (var providerTest, var setProviderTest) = UseState<ProviderTestResult?>(null);
        var initialDraft = UseMemo(
            () =>
            {
                var state = setup.LoadGitHubAppState();
                return new GitHubAppDraft(
                    AppId: state.AppId,
                    InstallationId: state.InstallationId,
                    WebhookUrl: state.WebhookUrl,
                    HasStoredPrivateKey: state.HasPrivateKey,
                    HasStoredWebhookSecret: state.HasWebhookSecret);
            },
            []);
        (var githubAppDraft, var setGithubAppDraft) = UseState(initialDraft);
        var wide = UseBreakpoint(720);
        (var windowWidth, var _) = UseWindowSize();
        var padding = wide ? 48 : 24;
        var contentWidth = Math.Min(640, Math.Max(0, windowWidth - (padding * 2)));

        Element content;
        if (step < 0)
        {
            content = Column(
                Math.Min(520, contentWidth),
                Welcome(wide, () => setStep(setup.IsPatConfigured ? 1 : 0)));
        }
        else
        {
            Element stepContent = step switch
            {
                0 => Component<PatStep, PatStepProps>(
                    new(
                        () => setStep(-1),
                        validation =>
                        {
                            if (validation is not null)
                            {
                                setVerifiedPat(validation);
                            }

                            setup.MarkStepCompleted(0);
                            setStep(1);
                        })),
                1 => Component<GitHubAppStep, GitHubAppStepProps>(
                    new(
                        githubAppDraft,
                        setGithubAppDraft,
                        verifiedPat,
                        () => setStep(0),
                        validation =>
                        {
                            setVerifiedApp(validation);
                            setup.MarkStepCompleted(1);
                            setStep(2);
                        })),
                2 => Component<ProviderStep, ProviderStepProps>(
                    new(
                        () => setStep(1),
                        result =>
                        {
                            setProviderTest(result);
                            setup.MarkStepCompleted(2);
                            setStep(3);
                        },
                        contentWidth,
                        verifiedApp)),
                _ => NextStepPlaceholder(
                    "Agent model",
                    ProviderSummary(providerTest),
                    () => setStep(2)),
            };

            content = Column(
                contentWidth,
                VStack(
                    12,
                    Caption($"STEP {step + 1} OF 6").Foreground(Theme.SecondaryText),
                    stepContent));
        }

        return VStack(0, content)
            .Padding(padding, 24);
    }

    private static int GetInitialStep(SetupService setup)
    {
        if (!setup.IsPatConfigured)
        {
            return setup.CompletedStep is null ? -1 : 0;
        }

        if (!setup.IsGitHubAppConfigured)
        {
            return 1;
        }

        var completed = Math.Max(setup.CompletedStep ?? 1, 1);
        return Math.Min(completed + 1, 5);
    }

    private static string ProviderSummary(ProviderTestResult? result)
    {
        return result switch
        {
            null => "Choose an agent model next.",
            { UsedFallback: true } =>
                "Provider connected — the key was verified with a chat completion. Choose an agent model next.",
            _ => $"Provider connected — {result.ModelCount} models available. Choose an agent model next.",
        };
    }

    private static GridElement Column(double width, Element content)
    {
        return Grid(
            [GridSize.Star(), GridSize.Px(width), GridSize.Star()],
            [GridSize.Auto],
            [content.Grid(row: 0, column: 1)]);
    }

    private static StackElement Welcome(bool wide, Action getStarted)
    {
        var heading = (wide ? TitleLarge("Welcome to Sebastian") : Title("Welcome to Sebastian"))
            .TextAlignment(TextAlignment.Center)
            .TextWrapping(TextWrapping.Wrap)
            .HeadingLevel(AutomationHeadingLevel.Level1);

        return VStack(
                0,
                heading,
                BodyLarge("Set up your workspace in a few steps. You can change everything later in Settings.")
                    .Foreground(Theme.SecondaryText)
                    .TextAlignment(TextAlignment.Center)
                    .TextWrapping(TextWrapping.Wrap)
                    .Margin(0, 12, 0, 0),
                Button("Get started", getStarted)
                    .AccentButton()
                    .MinWidth(160)
                    .Height(40)
                    .HAlign(HorizontalAlignment.Center)
                    .Margin(0, 40, 0, 0)
                    .AutomationName("Get started with Sebastian"));
    }

    private static StackElement NextStepPlaceholder(string title, string description, Action back)
    {
        return VStack(
            8,
            Title(title)
                .TextWrapping(TextWrapping.Wrap)
                .HeadingLevel(AutomationHeadingLevel.Level1),
            Body(description)
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.Wrap),
            Button("Back", back)
                .SubtleButton()
                .HAlign(HorizontalAlignment.Left)
                .Margin(0, 24, 0, 0));
    }
}
