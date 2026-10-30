#pragma warning disable MAAI001 // HarnessAgent and file access APIs are experimental.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI experimental extensions.

namespace Sebastian.Agent.Roles;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;

internal sealed class ImplementationAgent(
    string name,
    string workspacePath) : AgentRole, IAsyncDisposable
{
    private const string Instructions =
        """
        You are Sebastian's implementation agent. Implement the requested change in the workspace.
        Work carefully, keep changes minimal, and verify with the repository's own tests.
        Never run git commands, commit, push, or create pull requests.
        """;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    private readonly LocalShellExecutor _shell = CreateShell(workspacePath);

    protected override void Configure(HarnessAgentOptions options)
    {
        options.Name = name;
        options.ChatOptions = new ChatOptions
        {
            Instructions = Instructions,
            Tools = [_shell.AsAIFunction(requireApproval: false, name: "shell")],
        };
        options.FileAccessStore = new FileSystemAgentFileStore(workspacePath);
        options.FileAccessProviderOptions = new FileAccessProviderOptions
        {
            DisableReadOnlyToolApproval = true,
            DisableWriteToolApproval = true,
        };
        options.DisableFileMemory = true;
        options.DisableWebSearch = true;
        options.AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" };
        options.AIContextProviders = [new ShellEnvironmentProvider(_shell)];
    }

    public ValueTask DisposeAsync() => _shell.DisposeAsync();

    private static LocalShellExecutor CreateShell(string worktreePath) =>
        new(new LocalShellExecutorOptions
        {
            WorkingDirectory = worktreePath,
            ConfineWorkingDirectory = true,
            CleanEnvironment = true,
            AcknowledgeUnsafe = true,
            Timeout = CommandTimeout,
            Policy = new ShellPolicy(denyList:
            [
                @"\brm\s+-rf\b",
                @"\bRemove-Item\b.*-Recurse",
                @"\bgit\s+(push|reset|clean|checkout\s+--)\b",
                @"\bgh\s+",
                @"\b(curl|wget|ssh|scp|schtasks)\b",
                @"\bInvoke-(WebRequest|RestMethod)\b",
                @"\b(Format-Volume|Clear-Disk|Initialize-Disk|Stop-Computer|Restart-Computer)\b",
                @"\breg\s+(add|delete)\b",
            ]),
            Environment = new Dictionary<string, string?>
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",
            },
        });
}
