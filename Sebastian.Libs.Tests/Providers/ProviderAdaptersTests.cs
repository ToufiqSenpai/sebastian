using Sebastian.Libs.Providers;

namespace Sebastian.Libs.Tests.Providers;

public sealed class ProviderAdaptersTests
{
    [Theory]
    [InlineData("@ai-sdk/openai-compatible", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@ai-sdk/openai", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@openrouter/ai-sdk-provider", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@ai-sdk/deepinfra", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@aihubmix/ai-sdk-provider", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@saladtechnologies-oss/ai-sdk-provider", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@jerome-benoit/sap-ai-provider-v2", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@ai-sdk/groq", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@ai-sdk/mistral", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@ai-sdk/xai", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@ai-sdk/perplexity", ProviderAdapter.OpenAiCompatible)]
    [InlineData("@ai-sdk/anthropic", ProviderAdapter.Anthropic)]
    [InlineData("@ai-sdk/azure", ProviderAdapter.Azure)]
    public void KnownNpmMapsToAnAdapter(string npm, ProviderAdapter expected)
    {
        Assert.Equal(expected, ProviderAdapters.Infer(npm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@ai-sdk/google")]
    [InlineData("@ai-sdk/google-vertex")]
    [InlineData("@ai-sdk/amazon-bedrock")]
    public void UnknownNpmHasNoAdapter(string? npm)
    {
        Assert.Null(ProviderAdapters.Infer(npm));
    }

    [Theory]
    [InlineData("openai", "https://api.openai.com/v1")]
    [InlineData("anthropic", "https://api.anthropic.com")]
    [InlineData("groq", "https://api.groq.com/openai/v1")]
    [InlineData("mistral", "https://api.mistral.ai/v1")]
    [InlineData("xai", "https://api.x.ai/v1")]
    [InlineData("google", "https://generativelanguage.googleapis.com/v1beta/openai/")]
    public void DefaultEndpointsMatchSpec(string providerId, string expected)
    {
        Assert.Equal(new Uri(expected, UriKind.Absolute), ProviderAdapters.DefaultBaseUrl(providerId));
    }

    [Fact]
    public void CatalogApiWinsOverDefaultEndpoint()
    {
        var provider = TestCatalog.Provider(
            "openrouter",
            npm: "@openrouter/ai-sdk-provider",
            api: "https://openrouter.ai/api/v1");

        Assert.Equal(
            new Uri("https://openrouter.ai/api/v1", UriKind.Absolute),
            ProviderAdapters.ResolveBaseUrl(provider));
    }

    [Fact]
    public void AzureEndpointComesFromResourceName()
    {
        var provider = TestCatalog.Provider(
            "azure",
            npm: "@ai-sdk/azure",
            env: ["AZURE_RESOURCE_NAME", "AZURE_API_KEY"]);

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AZURE_RESOURCE_NAME"] = "my-resource",
        };

        Assert.Equal(
            new Uri("https://my-resource.openai.azure.com/openai/v1", UriKind.Absolute),
            ProviderAdapters.ResolveBaseUrl(provider, env));
    }

    [Fact]
    public void AzureEndpointRequiresValidResourceName()
    {
        Assert.Null(ProviderAdapters.BuildAzureBaseUrl("bad.resource"));
        Assert.Null(ProviderAdapters.BuildAzureBaseUrl("-bad"));
        Assert.Null(ProviderAdapters.BuildAzureBaseUrl(""));
        Assert.Equal(
            new Uri("https://good-resource.openai.azure.com/openai/v1", UriKind.Absolute),
            ProviderAdapters.BuildAzureBaseUrl("good-resource"));
    }

    [Theory]
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("HTTPS://api.example.com/v1", true)]
    [InlineData("http://127.0.0.1:5099/v1", true)]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("http://api.example.com/v1", false)]
    [InlineData("https://user:secret@api.example.com", false)]
    [InlineData("https://api.example.com/${RESOURCE}", false)]
    [InlineData("/relative", false)]
    [InlineData("", false)]
    public void BaseUrlValidationFollowsRules(string value, bool expected)
    {
        Assert.Equal(expected, ProviderAdapters.ValidateBaseUrl(value).IsValid);
    }

    [Fact]
    public void BaseUrlValidationNormalizesTrailingSlash()
    {
        var result = ProviderAdapters.ValidateBaseUrl("https://api.example.com/v1/");

        Assert.True(result.IsValid);
        Assert.Equal(new Uri("https://api.example.com/v1", UriKind.Absolute), result.Url);
    }

    [Fact]
    public void DocumentedHostUsesCatalogEndpointBeforeDocumentation()
    {
        var provider = TestCatalog.Provider(
            "openai",
            npm: "@ai-sdk/openai",
            doc: "https://platform.openai.com/docs/models");

        Assert.Equal("api.openai.com", ProviderAdapters.DocumentedHost(provider));
        Assert.False(
            ProviderAdapters.NeedsHostConfirmation(
                new Uri("https://api.openai.com/v1", UriKind.Absolute),
                provider));
        Assert.True(
            ProviderAdapters.NeedsHostConfirmation(
                new Uri("https://proxy.example/v1", UriKind.Absolute),
                provider));
    }

    [Fact]
    public void AzureDocumentedHostUsesDerivedEndpoint()
    {
        var provider = TestCatalog.Provider(
            "azure",
            npm: "@ai-sdk/azure",
            env: ["AZURE_RESOURCE_NAME", "AZURE_API_KEY"]);
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AZURE_RESOURCE_NAME"] = "my-resource",
        };

        Assert.False(
            ProviderAdapters.NeedsHostConfirmation(
                new Uri("https://my-resource.openai.azure.com/openai/v1", UriKind.Absolute),
                provider,
                env));
        Assert.True(
            ProviderAdapters.NeedsHostConfirmation(
                new Uri("https://other.openai.azure.com/openai/v1", UriKind.Absolute),
                provider,
                env));
    }

    [Theory]
    [InlineData(ProviderAdapter.OpenAiCompatible, "openai")]
    [InlineData(ProviderAdapter.Anthropic, "anthropic")]
    [InlineData(ProviderAdapter.Azure, "azure")]
    public void ApiNamesRoundTrip(ProviderAdapter adapter, string api)
    {
        Assert.Equal(api, ProviderAdapters.ToApi(adapter));
        Assert.Equal(adapter, ProviderAdapters.ParseApi(api));
        Assert.Equal(adapter, ProviderAdapters.FromIndex(ProviderAdapters.ToIndex(adapter)));
    }
}
