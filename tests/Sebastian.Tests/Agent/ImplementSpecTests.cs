namespace Sebastian.Tests.Agent;

using Sebastian.Agent;

public sealed class ImplementSpecTests
{
    [Fact]
    public async Task ReusesExistingWorktreeAndKeepsUncommittedWork()
    {
        using var repository = TestRepository.Create();
        var git = repository.CreateGit();
        await git.StartAsync(CancellationToken.None);
        var spec = new ImplementSpec("owner", "repo", 1, "First", "Body");

        var first = await spec.PrepareWorkspaceAsync(git, CancellationToken.None);
        var marker = Path.Combine(first, "agent-work.txt");
        File.WriteAllText(marker, "uncommitted");

        var second = await spec.PrepareWorkspaceAsync(git, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task RecreatesWorktreeWhenDirectoryWasRemoved()
    {
        using var repository = TestRepository.Create();
        var git = repository.CreateGit();
        await git.StartAsync(CancellationToken.None);
        var spec = new ImplementSpec("owner", "repo", 1, "First", "Body");

        var first = await spec.PrepareWorkspaceAsync(git, CancellationToken.None);
        TestRepository.RunGit(repository.Work, "worktree", "remove", "--force", first);

        var second = await spec.PrepareWorkspaceAsync(git, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public async Task RejectsDirtyMain()
    {
        using var repository = TestRepository.Create();
        var git = repository.CreateGit();
        await git.StartAsync(CancellationToken.None);
        File.AppendAllText(Path.Combine(repository.Work, "README.md"), "dirty");
        var spec = new ImplementSpec("owner", "repo", 1, "First", "Body");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => spec.PrepareWorkspaceAsync(git, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsForeignRepository()
    {
        using var repository = TestRepository.Create();
        var git = repository.CreateGit();
        await git.StartAsync(CancellationToken.None);
        var spec = new ImplementSpec("other", "repo", 1, "First", "Body");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => spec.PrepareWorkspaceAsync(git, CancellationToken.None));
    }
}
