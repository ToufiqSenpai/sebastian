namespace Sebastian.Tests.Agent;

using Microsoft.Extensions.Options;
using Sebastian.Agent;
using Sebastian.Configuration;

public sealed class ProviderResolverTests
{
    [Fact]
    public void SelectedProviderCreatesChatClient()
    {
        var resolver = CreateResolver();

        var client = resolver.CreateChatClient();

        Assert.NotNull(client);
        (client as IDisposable)?.Dispose();
    }

    [Fact]
    public void MissingProviderFails()
    {
        var config = CreateConfig(providerName: "missing");

        var exception = Assert.Throws<InvalidOperationException>(() => new ProviderResolver(Options.Create(config)));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateProviderNamesFail()
    {
        var config = CreateConfig();
        config.Agent.Providers.Add(new ProviderConfig
        {
            Name = "deepseek",
            Endpoint = "https://api.deepseek.com/v1",
            ApiKey = "test",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => new ProviderResolver(Options.Create(config)));

        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingApiKeyFails()
    {
        var config = CreateConfig();
        config.Agent.Providers[0].ApiKey = " ";

        Assert.Throws<InvalidOperationException>(() => new ProviderResolver(Options.Create(config)));
    }

    private static ProviderResolver CreateResolver() =>
        new(Options.Create(CreateConfig()));

    private static SebastianConfig CreateConfig(string providerName = "deepseek") => new()
    {
        GitHub = null!,
        Agent = new AgentConfig
        {
            Name = "Sebastian",
            General = new AgentGeneralConfig
            {
                Provider = providerName,
                Model = "deepseek-v4.1-flash",
            },
            Providers =
            [
                new ProviderConfig
                {
                    Name = "deepseek",
                    Endpoint = "https://api.deepseek.com/v1",
                    ApiKey = "test",
                },
            ],
        },
    };
}
