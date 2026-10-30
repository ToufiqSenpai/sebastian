namespace Sebastian.Tests.Configuration;

using Microsoft.Extensions.Configuration;
using Sebastian.Configuration;

public sealed class EnvironmentPlaceholdersTests
{
    [Fact]
    public void PlaceholderIsResolvedFromEnvironment()
    {
        var name = $"SEBASTIAN_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "resolved");

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:WebhookSecret"] = $"{{env:{name}}}",
                ["Agent:General:Model"] = "literal",
            });

            configuration.ExpandEnvironmentPlaceholders();

            Assert.Equal("resolved", configuration["GitHub:WebhookSecret"]);
            Assert.Equal("literal", configuration["Agent:General:Model"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void MissingEnvironmentVariableFails()
    {
        var name = $"SEBASTIAN_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, null);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Providers:0:ApiKey"] = $"{{env:{name}}}",
        });

        var exception = Assert.Throws<InvalidOperationException>(
            configuration.ExpandEnvironmentPlaceholders);

        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Agent:Providers:0:ApiKey", exception.Message, StringComparison.Ordinal);
    }
}
