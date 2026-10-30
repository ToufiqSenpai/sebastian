namespace Sebastian.Libs;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sebastian.Agent;

internal sealed partial class Git : IDisposable
{
    private const string BaseBranch = "main";
    private const string BaseRef = "origin/main";

    private readonly string _root;
    private readonly SemaphoreSlim _prepareLock = new(1, 1);
    private readonly ILogger<Git> _logger;
    private RepositoryIdentity? _repository;

    public Git(IHostEnvironment environment, ILogger<Git> logger)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ContentRootPath));
        _logger = logger;
    }

    public void Validate()
    {
        var topLevel = RunGitSync(["rev-parse", "--show-toplevel"]).Trim();
        if (!PathEquals(topLevel, _root))
        {
            throw new InvalidOperationException(
                $"Sebastian must run from the repository root. Content root is '{_root}', git root is '{topLevel}'.");
        }

        var remote = RunGitSync(["remote", "get-url", "origin"]).Trim();
        _repository = RepositoryIdentity.Parse(remote)
            ?? throw new InvalidOperationException("Could not parse the origin remote URL.");

        LogRepository(_repository.Owner, _repository.Name, BaseBranch);
    }

    public async Task<WorktreeInfo> PrepareAsync(ImplementationJob job, CancellationToken cancellationToken)
    {
        var repository = _repository
            ?? throw new InvalidOperationException("Git.Validate() was not called.");

        if (!string.Equals(job.Owner, repository.Owner, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(job.Repository, repository.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Issue repository '{job.Owner}/{job.Repository}' does not match local origin '{repository.Owner}/{repository.Name}'.");
        }

        await _prepareLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LogFetching(job.Repository);
            await RunGitAsync(["fetch", "--prune", "origin"], cancellationToken).ConfigureAwait(false);

            var baseSha = (await RunGitAsync(["rev-parse", "--verify", BaseRef], cancellationToken).ConfigureAwait(false)).Trim();
            LogBaseResolved(BaseRef, baseSha);

            var divergence = await TryRunGitAsync(
                ["rev-list", "--left-right", "--count", $"main...{BaseRef}"],
                cancellationToken).ConfigureAwait(false);
            if (divergence is not null)
            {
                var divergenceText = divergence.Trim();
                LogDivergence(divergenceText);
            }

            var workspaceRoot = Path.Combine(_root, ".sebastian", "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            var number = job.IssueNumber.ToString(CultureInfo.InvariantCulture);
            var worktreePath = Path.Combine(workspaceRoot, number);
            var branch = $"sebastian/issue-{number}";

            await RemoveWorktreeAsync(worktreePath, branch, cancellationToken).ConfigureAwait(false);
            await RunGitAsync(
                ["worktree", "add", worktreePath, "-b", branch, BaseRef],
                cancellationToken).ConfigureAwait(false);

            LogWorktreeReady(worktreePath, branch);
            return new WorktreeInfo(worktreePath, branch, baseSha);
        }
        finally
        {
            _prepareLock.Release();
        }
    }

    public async Task<string> GetStatusAsync(string worktreePath, CancellationToken cancellationToken)
    {
        var status = await RunGitAsync(
            ["-C", worktreePath, "status", "--short"],
            cancellationToken).ConfigureAwait(false);

        return status.Trim();
    }

    private async Task RemoveWorktreeAsync(
        string worktreePath,
        string branch,
        CancellationToken cancellationToken)
    {
        _ = await TryRunGitAsync(
            ["worktree", "remove", "--force", worktreePath],
            cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(worktreePath))
        {
            Directory.Delete(worktreePath, recursive: true);
        }

        _ = await TryRunGitAsync(["worktree", "prune"], cancellationToken).ConfigureAwait(false);
        _ = await TryRunGitAsync(["branch", "-D", branch], cancellationToken).ConfigureAwait(false);

        LogWorktreeRemoved(worktreePath);
    }

    private string RunGitSync(IReadOnlyList<string> arguments)
    {
        using var process = CreateProcess(arguments);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr.Trim()}");
        }

        return stdout;
    }

    private async Task<string> RunGitAsync(
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

    private async Task<string?> TryRunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RunGitAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
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

    public void Dispose()
    {
        _prepareLock.Dispose();
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Using repository {Owner}/{Repository} on {BaseBranch}")]
    private partial void LogRepository(string owner, string repository, string baseBranch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetching origin before preparing a worktree for {Repository}")]
    private partial void LogFetching(string repository);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolved {BaseRef} at {Sha}")]
    private partial void LogBaseResolved(string baseRef, string sha);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local main divergence (left=main, right=origin/main): {Counts}")]
    private partial void LogDivergence(string counts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worktree ready at {Path} on {Branch}")]
    private partial void LogWorktreeReady(string path, string branch);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleared previous worktree at {Path}")]
    private partial void LogWorktreeRemoved(string path);
}

internal sealed record WorktreeInfo(string Path, string Branch, string BaseSha);
