using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.Providers;
using Sebastian.Libs.Settings;
using Sebastian.Libs.Tests.GitHub.Personal;
using Sebastian.Libs.Tests.Settings;

namespace Sebastian.Libs.Tests.Providers;

public sealed class ProviderSetupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sebastian-tests",
        Guid.NewGuid().ToString("N"));

    private readonly InMemorySettings _settings = new();

    [Fact]
    public async Task PassingTestStoresProviderAndKeyWithoutOverrides()
    {
        var store = CreateStore();
        using var tester = CreateTester(OkModels);
        var setup = new ProviderSetupService(store, tester);
        var provider = OpenAiProvider();

        var result = await setup.TestAndSaveAsync(
            new ProviderSelection(
                provider,
                ProviderAdapter.OpenAiCompatible,
                "https://api.openai.com/v1",
                IsCustomEndpoint: false,
                new ProviderCredentials("sk-test", EmptyEnv())),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("openai", store["agent:provider"]);
        Assert.Equal("sk-test", store["agent:openai:api-key"]);
        Assert.False(store.Has("agent:openai:api"));
        Assert.False(store.Has("agent:openai:base-url"));
    }

    [Fact]
    public async Task FailedTestKeepsThePreviousKey()
    {
        var store = CreateStore();
        store.Set("agent:openai:api-key", "old-key", isSecret: true);
        using var tester = CreateTester(
            (_, _) => Task.FromResult(
                Json(HttpStatusCode.Unauthorized, """{"error":"bad key"}""")));
        var setup = new ProviderSetupService(store, tester);

        var result = await setup.TestAndSaveAsync(
            new ProviderSelection(
                OpenAiProvider(),
                ProviderAdapter.OpenAiCompatible,
                "https://api.openai.com/v1",
                IsCustomEndpoint: false,
                new ProviderCredentials("new-key", EmptyEnv())),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("old-key", store["agent:openai:api-key"]);
        Assert.False(store.Has("agent:provider"));
    }

    [Fact]
    public async Task BlankCandidateReusesTheStoredKey()
    {
        var store = CreateStore();
        store.Set("agent:openai:api-key", "stored-key", isSecret: true);
        using var tester = CreateTester(
            (request, _) =>
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("stored-key", request.Headers.Authorization?.Parameter);
                return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[]}"""));
            });
        var setup = new ProviderSetupService(store, tester);

        var result = await setup.TestAndSaveAsync(
            new ProviderSelection(
                OpenAiProvider(),
                ProviderAdapter.OpenAiCompatible,
                "https://api.openai.com/v1",
                IsCustomEndpoint: false,
                new ProviderCredentials(null, EmptyEnv())),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("stored-key", store["agent:openai:api-key"]);
    }

    [Fact]
    public async Task AzureResourceIsPlainAndKeyIsSecret()
    {
        var store = CreateStore();
        using var tester = CreateTester(
            (request, _) =>
            {
                Assert.Equal(
                    "https://my-resource.openai.azure.com/openai/v1/models",
                    request.RequestUri!.AbsoluteUri);
                Assert.True(request.Headers.TryGetValues("api-key", out var keys));
                Assert.Equal("azure-key", keys.Single());
                return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[{"id":"deployment"}]}"""));
            });
        var setup = new ProviderSetupService(store, tester);
        var provider = TestCatalog.Provider(
            "azure",
            npm: "@ai-sdk/azure",
            env: ["AZURE_RESOURCE_NAME", "AZURE_API_KEY"]);
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AZURE_RESOURCE_NAME"] = "my-resource",
        };

        var result = await setup.TestAndSaveAsync(
            new ProviderSelection(
                provider,
                ProviderAdapter.Azure,
                "https://my-resource.openai.azure.com/openai/v1",
                IsCustomEndpoint: false,
                new ProviderCredentials("azure-key", env)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("my-resource", _settings.Read("agent:azure:env:AZURE_RESOURCE_NAME"));
        Assert.False(_settings.Has("agent:azure:api-key"));
        Assert.Equal("azure-key", store["agent:azure:api-key"]);
        Assert.False(store.Has("agent:azure:base-url"));

        var stored = setup.GetStoredState(provider);
        Assert.True(stored.HasApiKey);
        Assert.Equal("my-resource", stored.PlainEnv["AZURE_RESOURCE_NAME"]);
    }

    [Fact]
    public async Task CustomEndpointStoresAndThenRemovesOverrides()
    {
        var store = CreateStore();
        using var tester = CreateTester(OkModels);
        var setup = new ProviderSetupService(store, tester);
        var provider = OpenAiProvider();

        var custom = await setup.TestAndSaveAsync(
            new ProviderSelection(
                provider,
                ProviderAdapter.Anthropic,
                "https://proxy.example/v1",
                IsCustomEndpoint: true,
                new ProviderCredentials("sk-test", EmptyEnv())),
            CancellationToken.None);

        Assert.True(custom.Success);
        Assert.Equal("anthropic", store["agent:openai:api"]);
        Assert.Equal("https://proxy.example/v1", store["agent:openai:base-url"]);

        var normal = await setup.TestAndSaveAsync(
            new ProviderSelection(
                provider,
                ProviderAdapter.OpenAiCompatible,
                "https://api.openai.com/v1",
                IsCustomEndpoint: false,
                new ProviderCredentials("sk-test", EmptyEnv())),
            CancellationToken.None);

        Assert.True(normal.Success);
        Assert.False(store.Has("agent:openai:api"));
        Assert.False(store.Has("agent:openai:base-url"));
    }

    [Fact]
    public async Task OtherProviderKeysAreNotTouched()
    {
        var store = CreateStore();
        store.Set("agent:anthropic:api-key", "other-key", isSecret: true);
        using var tester = CreateTester(OkModels);
        var setup = new ProviderSetupService(store, tester);

        _ = await setup.TestAndSaveAsync(
            new ProviderSelection(
                OpenAiProvider(),
                ProviderAdapter.OpenAiCompatible,
                "https://api.openai.com/v1",
                IsCustomEndpoint: false,
                new ProviderCredentials("sk-test", EmptyEnv())),
            CancellationToken.None);

        Assert.Equal("other-key", store["agent:anthropic:api-key"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CatalogProvider OpenAiProvider()
    {
        return TestCatalog.Provider(
            "openai",
            npm: "@ai-sdk/openai",
            env: ["OPENAI_API_KEY"]);
    }

    private static Dictionary<string, string> EmptyEnv()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private KeyValueStore CreateStore()
    {
        return new KeyValueStore(_settings, _directory);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The tester owns and disposes the handler.")]
    private static ProviderTester CreateTester(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new ProviderTester(new StubHttpMessageHandler(handler), TimeSpan.FromSeconds(5));
    }

    private static Task<HttpResponseMessage> OkModels(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[{"id":"gpt-test"}]}"""));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
