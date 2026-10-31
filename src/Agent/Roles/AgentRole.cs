#pragma warning disable MAAI001 // HarnessAgent and file access APIs are experimental.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI experimental extensions.

namespace Sebastian.Agent.Roles;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

internal abstract class AgentRole
{
    internal const string Persona = "You are Sebastian, an autonomous coding agent.";

    public HarnessAgent CreateAgent(IChatClient chatClient) =>
        chatClient.AsHarnessAgent(CreateOptions());

    internal HarnessAgentOptions CreateOptions()
    {
        var options = new HarnessAgentOptions();
        Configure(options);

        var role = options.ChatOptions?.Instructions;
        options.ChatOptions ??= new ChatOptions();
        options.ChatOptions.Instructions = string.IsNullOrWhiteSpace(role)
            ? Persona
            : $"{Persona}\n\n{role}";

        return options;
    }

    protected abstract void Configure(HarnessAgentOptions options);
}
