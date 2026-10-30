namespace Sebastian.Tests;

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Sebastian.Libs;

internal sealed class TestRepository : IDisposable
{
    private readonly string _root;
    private readonly string _origin;
    private readonly string _work;

    private TestRepository(string root, string origin, string work)
    {
        _root = root;
        _origin = origin;
        _work = work;
    }

    public string Work => _work;

    public static TestRepository Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "sebastian-tests", Guid.NewGuid().ToString("N"));
        var origin = Path.Combine(root, "owner", "repo.git");
        Directory.CreateDirectory(Path.GetDirectoryName(origin)!);

        RunGit(root, "init", "--bare", "--initial-branch=main", origin);
        var work = Path.Combine(root, "work");
        RunGit(root, "clone", FileUrl(origin), work);
        Configure(work);

        File.WriteAllText(Path.Combine(work, "README.md"), "initial");
        RunGit(work, "add", "README.md");
        RunGit(work, "commit", "-m", "initial");
        RunGit(work, "push", "origin", "main");

        return new TestRepository(root, origin, work);
    }

    public Git CreateGit() => new(new TestHostEnvironment(_work), NullLogger<Git>.Instance);

    public string LocalHead() => RevParse(_work, "main");

    public string RemoteHead() => RevParse(_origin, "main");

    public string RemoteTrackingHead() => RevParse(_work, "origin/main");

    public string CloneAgain()
    {
        var path = Path.Combine(_root, $"clone-{Guid.NewGuid():N}");
        RunGit(_root, "clone", FileUrl(_origin), path);
        Configure(path);
        return path;
    }

    public void CommitFile(string repositoryPath, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(repositoryPath, file), content);
        RunGit(repositoryPath, "add", file);
        RunGit(repositoryPath, "commit", "-m", message);
    }

    public static string HeadAt(string path) => RevParse(path, "HEAD");

    public static string RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr.Trim()}");
        }

        return stdout;
    }

    private static string RevParse(string path, string revision) =>
        RunGit(path, "rev-parse", revision).Trim();

    private static string FileUrl(string path) => new Uri(path).AbsoluteUri;

    private static void Configure(string path)
    {
        RunGit(path, "config", "user.name", "Test");
        RunGit(path, "config", "user.email", "test@example.com");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }
}
