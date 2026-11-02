using Sebastian.Libs.Providers;

namespace Sebastian.Libs.Tests.Providers;

public sealed class ProviderFieldsTests
{
    [Fact]
    public void SingleKeyBecomesPrimaryApiKey()
    {
        var provider = TestCatalog.Provider(
            "openai",
            env: ["OPENAI_API_KEY"]);

        var fields = ProviderFields.Build(provider);

        var field = Assert.Single(fields);
        Assert.Equal("OPENAI_API_KEY", field.Name);
        Assert.Equal("API key", field.Label);
        Assert.True(field.IsPrimaryKey);
        Assert.True(field.IsSecret);
        Assert.Empty(field.Aliases);
    }

    [Fact]
    public void GoogleAliasesCollapseIntoOneField()
    {
        var provider = TestCatalog.Provider(
            "google",
            env:
            [
                "GOOGLE_API_KEY",
                "GOOGLE_GENERATIVE_AI_API_KEY",
                "GEMINI_API_KEY",
            ]);

        var field = Assert.Single(ProviderFields.Build(provider));

        Assert.Equal("GOOGLE_API_KEY", field.Name);
        Assert.True(field.IsPrimaryKey);
        Assert.Equal(3, field.Aliases.Count);
        Assert.Contains("GEMINI_API_KEY", field.Aliases);
    }

    [Fact]
    public void AzureResourceIsPlainAndKeyIsPrimary()
    {
        var provider = TestCatalog.Provider(
            "azure",
            npm: "@ai-sdk/azure",
            env: ["AZURE_RESOURCE_NAME", "AZURE_API_KEY"]);

        var fields = ProviderFields.Build(provider);

        Assert.Equal(2, fields.Count);
        Assert.Equal("AZURE_RESOURCE_NAME", fields[0].Name);
        Assert.False(fields[0].IsSecret);
        Assert.False(fields[0].IsPrimaryKey);
        Assert.Equal("AZURE_API_KEY", fields[1].Name);
        Assert.True(fields[1].IsSecret);
        Assert.True(fields[1].IsPrimaryKey);
    }

    [Fact]
    public void MultipleSecretsWithConfigStaySeparate()
    {
        var provider = TestCatalog.Provider(
            "amazon-bedrock",
            npm: "@ai-sdk/amazon-bedrock",
            env:
            [
                "AWS_ACCESS_KEY_ID",
                "AWS_SECRET_ACCESS_KEY",
                "AWS_REGION",
            ]);

        var fields = ProviderFields.Build(provider);

        Assert.Equal(3, fields.Count);
        Assert.True(fields[0].IsPrimaryKey);
        Assert.True(fields[1].IsSecret);
        Assert.False(fields[2].IsSecret);
    }

    [Fact]
    public void EmptyEnvFallsBackToApiKey()
    {
        var provider = TestCatalog.Provider("custom", env: []);

        var field = Assert.Single(ProviderFields.Build(provider));

        Assert.Equal("API_KEY", field.Name);
        Assert.True(field.IsPrimaryKey);
    }

    [Theory]
    [InlineData("AZURE_API_KEY", true)]
    [InlineData("AZURE_RESOURCE_NAME", false)]
    [InlineData("GEMINI_API_KEY", true)]
    [InlineData("AWS_SECRET_ACCESS_KEY", true)]
    [InlineData("GOOGLE_APPLICATION_CREDENTIALS", true)]
    [InlineData("AWS_REGION", false)]
    public void SecretClassificationFollowsName(string envName, bool expected)
    {
        Assert.Equal(expected, ProviderFields.IsSecretName(envName));
    }

    [Theory]
    [InlineData("AZURE_RESOURCE_NAME", "Azure Resource Name")]
    [InlineData("AZURE_API_KEY", "Azure API Key")]
    [InlineData("GOOGLE_GENERATIVE_AI_API_KEY", "Google Generative AI API Key")]
    [InlineData("AWS_ACCESS_KEY_ID", "AWS Access Key ID")]
    public void LabelsAreReadable(string envName, string expected)
    {
        Assert.Equal(expected, ProviderFields.ToLabel(envName));
    }
}
