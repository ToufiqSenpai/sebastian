namespace Sebastian.Libs.GitHub.App;

public sealed record GitHubAppCredentials(
    string AppId,
    string InstallationId,
    string PrivateKey,
    string WebhookSecret,
    Uri? WebhookUrl);

public sealed record GitHubAppStoredState(
    string AppId,
    string InstallationId,
    Uri? WebhookUrl,
    bool HasPrivateKey,
    bool HasWebhookSecret);
