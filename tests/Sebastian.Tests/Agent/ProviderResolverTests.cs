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
    public void MissingProviderFailsValidation()
    {
        var config = CreateConfig(providerName: "missing");

        var result = Validate(config);

        Assert.True(result.Failed);
        Assert.Contains("Agent provider 'missing' is not configured.", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateProviderNamesFailValidation()
    {
        var config = CreateConfig();
        config.Agent.Providers.Add(new ProviderConfig
        {
            Name = "deepseek",
            Endpoint = "https://api.deepseek.com/v1",
            ApiKey = "test",
        });

        var result = Validate(config);

        Assert.True(result.Failed);
        Assert.Contains("Agent provider 'deepseek' is configured more than once.", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyProvidersFailValidation()
    {
        var config = CreateConfig();
        config.Agent.Providers.Clear();

        var result = Validate(config);

        Assert.True(result.Failed);
        Assert.Contains("At least one agent provider must be configured.", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidEndpointFailsValidation()
    {
        var config = CreateConfig();
        config.Agent.Providers[0].Endpoint = "not-a-uri";

        var result = Validate(config);

        Assert.True(result.Failed);
        Assert.Contains("Agent provider 'deepseek' has an invalid endpoint.", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankApiKeyIsRejectedByGeneratedValidator()
    {
        var config = CreateConfig();
        var validator = new ValidateSebastianConfig();

        var valid = validator.Validate(null, config);
        Assert.False(valid.Failed);

        config.Agent.Providers[0].ApiKey = " ";

        var invalid = validator.Validate(null, config);
        Assert.True(invalid.Failed);
    }

    private static ValidateOptionsResult Validate(SebastianConfig config) =>
        new ValidateSebastianConfig().Validate(null, config);

    private static ProviderResolver CreateResolver() =>
        new(Options.Create(CreateConfig()));

    private static SebastianConfig CreateConfig(string providerName = "deepseek") => new()
    {
        GitHub = new GitHubConfig
        {
            WebhookSecret = "secret",
            AppId = 1,
            PrivateKeyPath = "private-key.pem",
            InstallationId = "1",
        },
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
