namespace Sebastian.Libs.GitHub.App;

public sealed record GitHubAppCredentials(
    string AppId,
    string InstallationId,
    string PrivateKey,
    string WebhookSecret,
    string WebhookUrl);

public sealed record GitHubAppStoredState(
    string AppId,
    string InstallationId,
    string WebhookUrl,
    bool HasPrivateKey,
    bool HasWebhookSecret);
