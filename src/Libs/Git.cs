namespace Sebastian.Libs;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed partial class Git : IHostedService
{
    public const string BaseBranch = "main";
    public const string BaseRef = "origin/main";

    private readonly string _root;
    private readonly ILogger<Git> _logger;
    private RepositoryIdentity? _repository;

    public Git(IHostEnvironment environment, ILogger<Git> logger)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ContentRootPath));
        _logger = logger;
    }

    public string Root => _root;

    public RepositoryIdentity? Repository => _repository;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync().ConfigureAwait(false);

        // Best-effort, bounded: a missed push webhook must not prevent the app from starting.
        // shortcut: push + startup sync only; add a periodic fetch if missed push webhooks become a problem.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await SyncMainAsync(timeout.Token).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureRepositoryAsync()
    {
        var topLevel = await GetTopLevelAsync(CancellationToken.None).ConfigureAwait(false);
        if (!PathEquals(topLevel, _root))
        {
            throw new InvalidOperationException(
                $"Sebastian must run from the repository root. Content root is '{_root}', git root is '{topLevel}'.");
        }

        var remote = await GetRemoteUrlAsync("origin", CancellationToken.None).ConfigureAwait(false);
        _repository = RepositoryIdentity.Parse(remote)
            ?? throw new InvalidOperationException("Could not parse the origin remote URL.");

        LogRepository(_repository.Owner, _repository.Name, BaseBranch);
    }

    public Task<string> GetTopLevelAsync(CancellationToken cancellationToken) =>
        RunTrimmedAsync(["rev-parse", "--show-toplevel"], cancellationToken);

    public Task<string> GetRemoteUrlAsync(string remote, CancellationToken cancellationToken) =>
        RunTrimmedAsync(["remote", "get-url", remote], cancellationToken);

    public Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken) =>
        RunTrimmedAsync(["branch", "--show-current"], cancellationToken);

    public async Task<bool> HasTrackedChangesAsync(CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(await RunAsync(
            ["status", "--porcelain", "--untracked-files=no"],
            cancellationToken).ConfigureAwait(false));

    public Task<string> GetCommitShaAsync(string revision, CancellationToken cancellationToken) =>
        RunTrimmedAsync(["rev-parse", revision], cancellationToken);

    public async Task<int> CountCommitsAsync(string range, CancellationToken cancellationToken)
    {
        var output = await RunTrimmedAsync(["rev-list", "--count", range], cancellationToken).ConfigureAwait(false);
        return int.Parse(output, CultureInfo.InvariantCulture);
    }

    public Task FetchAsync(string remote, CancellationToken cancellationToken) =>
        RunAsync(["fetch", "--prune", remote], cancellationToken);

    public Task MergeFastForwardAsync(string reference, CancellationToken cancellationToken) =>
        RunAsync(["merge", "--ff-only", reference], cancellationToken);

    public Task AddWorktreeAsync(
        string path,
        string branch,
        string startPoint,
        CancellationToken cancellationToken) =>
        RunAsync(["worktree", "add", path, "-b", branch, startPoint], cancellationToken);

    public async Task RemoveWorktreeAsync(
        string worktreePath,
        string branch,
        CancellationToken cancellationToken)
    {
        await TryAsync(["worktree", "remove", "--force", worktreePath], cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(worktreePath))
        {
            Directory.Delete(worktreePath, recursive: true);
        }

        await TryAsync(["worktree", "prune"], cancellationToken).ConfigureAwait(false);
        await TryAsync(["branch", "-D", branch], cancellationToken).ConfigureAwait(false);

        LogWorktreeRemoved(worktreePath);
    }

    public async Task<string> GetStatusAsync(string worktreePath, CancellationToken cancellationToken)
    {
        var status = await RunAsync(
            ["-C", worktreePath, "status", "--short"],
            cancellationToken).ConfigureAwait(false);

        return status.Trim();
    }

    public Task SyncMainAsync(CancellationToken cancellationToken) =>
        SyncMainAsync($"refs/heads/{BaseBranch}", deleted: false, cancellationToken);

    public async Task SyncMainAsync(
        string refName,
        bool deleted,
        CancellationToken cancellationToken)
    {
        if (deleted ||
            !string.Equals(refName, $"refs/heads/{BaseBranch}", StringComparison.Ordinal))
        {
            return;
        }

        // shortcut: worktree prep can run during a fast-forward; worst case the prep fails on git's index.lock, the repo stays consistent.
        try
        {
            LogSyncFetching();
            await FetchAsync("origin", cancellationToken).ConfigureAwait(false);

            var problem = await FindMirrorProblemAsync(cancellationToken).ConfigureAwait(false);
            if (problem is not null)
            {
                LogSyncSkipped(problem);
                return;
            }

            var behind = await CountCommitsAsync($"{BaseBranch}..{BaseRef}", cancellationToken).ConfigureAwait(false);
            if (behind == 0)
            {
                LogMainAlreadyCurrent(BaseBranch, BaseRef);
                return;
            }

            var before = await GetCommitShaAsync(BaseBranch, cancellationToken).ConfigureAwait(false);
            await MergeFastForwardAsync(BaseRef, cancellationToken).ConfigureAwait(false);
            var after = await GetCommitShaAsync(BaseBranch, cancellationToken).ConfigureAwait(false);
            LogMainFastForwarded(BaseBranch, before, after);
        }
        catch (InvalidOperationException exception)
        {
            LogSyncFailed(exception);
        }
        catch (OperationCanceledException)
        {
            LogSyncCancelled();
        }
    }

    public async Task<string?> FindMirrorProblemAsync(CancellationToken cancellationToken)
    {
        var branch = await GetCurrentBranchAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(branch, BaseBranch, StringComparison.Ordinal))
        {
            return $"Local {BaseBranch} is not checked out (current branch: '{branch}'). Run Sebastian from {BaseBranch}.";
        }

        if (await HasTrackedChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            return $"Local {BaseBranch} has uncommitted tracked changes. Commit or stash them, or move them to a branch.";
        }

        var ahead = await CountCommitsAsync($"{BaseRef}..{BaseBranch}", cancellationToken).ConfigureAwait(false);
        if (ahead > 0)
        {
            return $"Local {BaseBranch} has {ahead} commit(s) not on {BaseRef}. Move them to a branch: git branch wip/local && git reset --hard {BaseRef}";
        }

        return null;
    }

    private async Task<string> RunTrimmedAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        (await RunAsync(arguments, cancellationToken).ConfigureAwait(false)).Trim();

    private async Task TryAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Best-effort cleanup command.
        }
    }

    private async Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = CreateProcess(arguments);
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr.Trim()}");
            }

            return stdout;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private Process CreateProcess(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        return new Process { StartInfo = startInfo };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using repository {Owner}/{Repository} on {BaseBranch}")]
    private partial void LogRepository(string owner, string repository, string baseBranch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Syncing local main from origin")]
    private partial void LogSyncFetching();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped local main sync: {Reason}")]
    private partial void LogSyncSkipped(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local {BaseBranch} already matches {BaseRef}")]
    private partial void LogMainAlreadyCurrent(string baseBranch, string baseRef);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fast-forwarded local {BaseBranch} from {Before} to {After}")]
    private partial void LogMainFastForwarded(string baseBranch, string before, string after);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local main sync failed")]
    private partial void LogSyncFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Local main sync cancelled")]
    private partial void LogSyncCancelled();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleared previous worktree at {Path}")]
    private partial void LogWorktreeRemoved(string path);
}
