#pragma warning disable MAAI001 // HarnessAgent and file access APIs are experimental.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI experimental extensions.

namespace Sebastian.Agent.Roles;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

internal abstract class AgentRole
{
    public HarnessAgent CreateAgent(IChatClient chatClient) =>
        chatClient.AsHarnessAgent(CreateOptions());

    internal HarnessAgentOptions CreateOptions()
    {
        var options = new HarnessAgentOptions();
        Configure(options);
        return options;
    }

    protected abstract void Configure(HarnessAgentOptions options);
}
