namespace Sebastian.Tests.Libs;

using Sebastian.Libs;

public sealed class RepositoryIdentityTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo")]
    [InlineData("https://github.com/owner/repo", "owner", "repo")]
    [InlineData("git@github.com:owner/repo.git", "owner", "repo")]
    [InlineData("ssh://git@github.com/owner/repo.git", "owner", "repo")]
    public void ParsesSupportedRemoteUrls(string remote, string owner, string name)
    {
        var identity = RepositoryIdentity.Parse(remote);

        Assert.NotNull(identity);
        Assert.Equal(owner, identity.Owner);
        Assert.Equal(name, identity.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-remote")]
    [InlineData("https://github.com/only-owner")]
    public void InvalidRemoteUrlsReturnNull(string remote)
    {
        Assert.Null(RepositoryIdentity.Parse(remote));
    }
}
