using Sebastian.Libs.Settings;

namespace Sebastian.Libs.GitHub.App;

public sealed class GitHubAppSetupService
{
    private readonly KeyValueStore _store;
    private readonly IGitHubAppService _github;

    public GitHubAppSetupService(KeyValueStore store, IGitHubAppService github)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(github);

        _store = store;
        _github = github;
    }

    public bool IsConfigured =>
        Has("github:app-id") &&
        Has("github:installation-id") &&
        Has("github:app-key") &&
        Has("github:webhook-secret");

    public GitHubAppStoredState LoadState()
    {
        var webhookUrl = _store["github:webhook-url"];

        return new GitHubAppStoredState(
            _store["github:app-id"] ?? string.Empty,
            _store["github:installation-id"] ?? string.Empty,
            Uri.TryCreate(webhookUrl, UriKind.Absolute, out var parsed) ? parsed : null,
            Has("github:app-key"),
            Has("github:webhook-secret"));
    }

    public async Task<GitHubAppValidation> TestAndSaveAsync(
        GitHubAppCredentials candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var appId = candidate.AppId.Trim();
        var installationId = candidate.InstallationId.Trim();
        var webhookUrl = candidate.WebhookUrl;
        var privateKey = string.IsNullOrWhiteSpace(candidate.PrivateKey)
            ? _store["github:app-key"]
            : candidate.PrivateKey;
        var webhookSecret = string.IsNullOrWhiteSpace(candidate.WebhookSecret)
            ? _store["github:webhook-secret"]
            : candidate.WebhookSecret;

        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return GitHubAppValidation.Failed("Choose a private key file.");
        }

        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            return GitHubAppValidation.Failed("Enter a webhook secret.");
        }

        var result = await _github
            .ValidateAsync(
                new GitHubAppCredentials(
                    appId,
                    installationId,
                    privateKey,
                    webhookSecret,
                    webhookUrl),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        _store.Set("github:app-id", appId, isSecret: false);
        _store.Set("github:installation-id", installationId, isSecret: false);

        if (!string.IsNullOrWhiteSpace(candidate.PrivateKey))
        {
            _store.Set("github:app-key", candidate.PrivateKey, isSecret: true);
        }

        if (!string.IsNullOrWhiteSpace(candidate.WebhookSecret))
        {
            _store.Set("github:webhook-secret", candidate.WebhookSecret, isSecret: true);
        }

        if (webhookUrl is null)
        {
            _store.Remove("github:webhook-url");
        }
        else
        {
            _store.Set(
                "github:webhook-url",
                webhookUrl.OriginalString.TrimEnd('/'),
                isSecret: false);
        }

        return result;
    }

    private bool Has(string key)
    {
        return !string.IsNullOrWhiteSpace(_store[key]);
    }
}
