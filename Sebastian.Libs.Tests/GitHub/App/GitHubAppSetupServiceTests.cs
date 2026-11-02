using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.Settings;
using Sebastian.Libs.Tests.Settings;

namespace Sebastian.Libs.Tests.GitHub.App;

public sealed class GitHubAppSetupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sebastian-tests",
        Guid.NewGuid().ToString("N"));

    private readonly InMemorySettings _settings = new();

    [Fact]
    public async Task PassingTestStoresPlainValuesAndSecrets()
    {
        var store = CreateStore();
        var fake = new FakeGitHubAppService();
        var setup = new GitHubAppSetupService(store, fake);

        var result = await setup.TestAndSaveAsync(
            new GitHubAppCredentials(
                "123",
                "42",
                "pem-key",
                "webhook-secret-value",
                new Uri("https://example.com/hook", UriKind.Absolute)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("123", _settings.Read("github:app-id"));
        Assert.Equal("42", _settings.Read("github:installation-id"));
        Assert.Equal("https://example.com/hook", _settings.Read("github:webhook-url"));
        Assert.False(_settings.Has("github:app-key"));
        Assert.Equal("pem-key", store["github:app-key"]);
        Assert.Equal("webhook-secret-value", store["github:webhook-secret"]);
        Assert.True(setup.IsConfigured);

        var state = setup.LoadState();
        Assert.Equal("123", state.AppId);
        Assert.Equal("42", state.InstallationId);
        Assert.Equal(
            new Uri("https://example.com/hook", UriKind.Absolute),
            state.WebhookUrl);
        Assert.True(state.HasPrivateKey);
        Assert.True(state.HasWebhookSecret);
    }

    [Fact]
    public async Task FailedTestWritesNothing()
    {
        var store = CreateStore();
        store.Set("github:app-id", "old", isSecret: false);
        store.Set("github:app-key", "old-key", isSecret: true);
        store.Set("github:webhook-secret", "old-secret", isSecret: true);

        var fake = new FakeGitHubAppService
        {
            Result = GitHubAppValidation.Failed("GitHub rejected the app."),
        };
        var setup = new GitHubAppSetupService(store, fake);

        var result = await setup.TestAndSaveAsync(
            new GitHubAppCredentials(
                "new",
                "42",
                "new-key",
                "new-secret-value",
                new Uri("https://example.com/new", UriKind.Absolute)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("old", store["github:app-id"]);
        Assert.Equal("old-key", store["github:app-key"]);
        Assert.Equal("old-secret", store["github:webhook-secret"]);
        Assert.False(store.Has("github:installation-id"));
        Assert.False(store.Has("github:webhook-url"));
    }

    [Fact]
    public async Task BlankCandidateUsesStoredSecrets()
    {
        var store = CreateStore();
        store.Set("github:app-key", "stored-key", isSecret: true);
        store.Set("github:webhook-secret", "stored-secret", isSecret: true);
        var fake = new FakeGitHubAppService();
        var setup = new GitHubAppSetupService(store, fake);

        var result = await setup.TestAndSaveAsync(
            new GitHubAppCredentials("123", "42", string.Empty, string.Empty, WebhookUrl: null),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(fake.LastCredentials);
        Assert.Equal("stored-key", fake.LastCredentials.PrivateKey);
        Assert.Equal("stored-secret", fake.LastCredentials.WebhookSecret);
    }

    [Fact]
    public async Task EmptyWebhookUrlRemovesTheStoredValue()
    {
        var store = CreateStore();
        store.Set("github:webhook-url", "https://example.com/hook", isSecret: false);
        var fake = new FakeGitHubAppService();
        var setup = new GitHubAppSetupService(store, fake);

        _ = await setup.TestAndSaveAsync(
            new GitHubAppCredentials(
                "123",
                "42",
                "pem-key",
                "webhook-secret-value",
                WebhookUrl: null),
            CancellationToken.None);

        Assert.False(store.Has("github:webhook-url"));
    }

    [Fact]
    public void ConfiguredRequiresEveryKey()
    {
        var store = CreateStore();
        var setup = new GitHubAppSetupService(store, new FakeGitHubAppService());

        store.Set("github:app-id", "123", isSecret: false);
        store.Set("github:installation-id", "42", isSecret: false);
        store.Set("github:app-key", "pem-key", isSecret: true);

        Assert.False(setup.IsConfigured);

        store.Set("github:webhook-secret", "webhook-secret-value", isSecret: true);

        Assert.True(setup.IsConfigured);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private KeyValueStore CreateStore()
    {
        return new KeyValueStore(_settings, _directory);
    }

    private sealed class FakeGitHubAppService : IGitHubAppService
    {
        public GitHubAppValidation Result { get; set; } = GitHubAppValidation.Passed(
            "Sebastian",
            "sebastian",
            "octocat",
            3,
            new Uri("https://github.com/apps/sebastian/installations/new", UriKind.Absolute));

        public GitHubAppCredentials? LastCredentials { get; private set; }

        public Task<GitHubAppValidation> ValidateAsync(
            GitHubAppCredentials credentials,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastCredentials = credentials;
            return Task.FromResult(Result);
        }
    }
}
