namespace Sebastian.Tests.Webhooks;

using Sebastian.Libs;
using Sebastian.Webhooks;

public sealed class GitHubWebhookProcessorTests
{
    [Fact]
    public void OnlyLocalRepositoryPasses()
    {
        var local = RepositoryIdentity.Parse("https://github.com/owner/repo.git");

        Assert.True(GitHubWebhookProcessor.IsLocalRepository(local, "OWNER", "REPO"));
        Assert.False(GitHubWebhookProcessor.IsLocalRepository(local, "owner", "other"));
        Assert.False(GitHubWebhookProcessor.IsLocalRepository(null, "owner", "repo"));
        Assert.False(GitHubWebhookProcessor.IsLocalRepository(local, null, null));
    }
}
