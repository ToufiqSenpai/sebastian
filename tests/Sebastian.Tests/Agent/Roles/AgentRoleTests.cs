#pragma warning disable MAAI001 // HarnessAgent and file access APIs are experimental.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI experimental extensions.

namespace Sebastian.Tests.Agent.Roles;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sebastian.Agent.Roles;

public sealed class AgentRoleTests
{
    [Fact]
    public void CreateOptionsUsesConfigure()
    {
        var role = new FakeRole();

        var options = role.CreateOptions();

        Assert.Equal("fake", options.Name);
        Assert.Equal("Fake instructions.", options.ChatOptions!.Instructions);
    }

    [Fact]
    public async Task ImplementationAgentConfiguresFileAccessAndShell()
    {
        await using var role = new ImplementationAgent("implementation", Path.GetTempPath());

        var options = role.CreateOptions();

        Assert.Equal("implementation", options.Name);
        Assert.NotNull(options.FileAccessStore);
        Assert.False(options.FileAccessProviderOptions!.DisableWriteTools);
        Assert.Single(options.AIContextProviders!);
        Assert.Single(options.ChatOptions!.Tools!);
    }

    private sealed class FakeRole : AgentRole
    {
        protected override void Configure(HarnessAgentOptions options)
        {
            options.Name = "fake";
            options.ChatOptions = new ChatOptions { Instructions = "Fake instructions." };
        }
    }
}
