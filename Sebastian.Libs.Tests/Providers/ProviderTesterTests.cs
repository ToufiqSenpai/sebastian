using System.Net;
using Sebastian.Libs.Providers;

namespace Sebastian.Libs.Tests.Providers;

public sealed class ProviderTesterTests
{
    [Fact]
    public async Task OpenAiModelListReturnsCount()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal(
                    "https://api.openai.com/v1/models",
                    request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("sk-test", request.Headers.Authorization?.Parameter);
                return Task.FromResult(TestHttp.Json(HttpStatusCode.OK, """{"data":[{"id":"a"},{"id":"b"}]}"""));
            });
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            new Uri("https://api.openai.com/v1", UriKind.Absolute),
            "sk-test",
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.ModelCount);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task AnthropicModelListUsesAnthropicHeaders()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(
                    "https://api.anthropic.com/v1/models",
                    request.RequestUri!.AbsoluteUri);
                Assert.True(request.Headers.TryGetValues("x-api-key", out var keys));
                Assert.Equal("sk-ant", keys.Single());
                Assert.True(request.Headers.TryGetValues("anthropic-version", out var versions));
                Assert.Equal("2023-06-01", versions.Single());
                return Task.FromResult(TestHttp.Json(HttpStatusCode.OK, """{"data":[{"id":"claude"}]}"""));
            });
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.Anthropic,
            new Uri("https://api.anthropic.com", UriKind.Absolute),
            "sk-ant",
            [TestCatalog.Model("claude")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModelCount);
    }

    [Fact]
    public async Task AnthropicBaseEndingInV1DoesNotDoubleThePath()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(
                    "https://api.anthropic.com/v1/models",
                    request.RequestUri!.AbsoluteUri);
                return Task.FromResult(TestHttp.Json(HttpStatusCode.OK, """{"data":[]}"""));
            });
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.Anthropic,
            new Uri("https://api.anthropic.com/v1", UriKind.Absolute),
            "sk-ant",
            [TestCatalog.Model("claude")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ModelCount);
    }

    [Fact]
    public async Task AzureModelListUsesApiKeyHeader()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(
                    "https://my-resource.openai.azure.com/openai/v1/models",
                    request.RequestUri!.AbsoluteUri);
                Assert.True(request.Headers.TryGetValues("api-key", out var keys));
                Assert.Equal("azure-key", keys.Single());
                Assert.Null(request.Headers.Authorization);
                return Task.FromResult(TestHttp.Json(HttpStatusCode.OK, """{"data":[{"id":"deployment"}]}"""));
            });
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.Azure,
            new Uri("https://my-resource.openai.azure.com/openai/v1", UriKind.Absolute),
            "azure-key",
            [TestCatalog.Model("deployment")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModelCount);
    }

    [Fact]
    public async Task Azure404DoesNotFallBackToChat()
    {
        var calls = 0;
        using var testerHandler = new StubHttpMessageHandler(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(TestHttp.Json(HttpStatusCode.NotFound, """{"error":"not found"}"""));
            });
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.Azure,
            new Uri("https://my-resource.openai.azure.com/openai/v1", UriKind.Absolute),
            "azure-key",
            [TestCatalog.Model("deployment")],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, calls);
        Assert.Contains("/openai/v1", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAi404FallsBackToChatCompletion()
    {
        string? body = null;
        using var testerHandler = new StubHttpMessageHandler(
            async (request, cancellationToken) =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    return TestHttp.Json(HttpStatusCode.NotFound, """{"error":"no models endpoint"}""");
                }

                Assert.Equal(
                    "https://api.openai.com/v1/chat/completions",
                    request.RequestUri!.AbsoluteUri);
                body = await request.Content!
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                return TestHttp.Json(HttpStatusCode.OK, "{}");
            });
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            new Uri("https://api.openai.com/v1", UriKind.Absolute),
            "sk-test",
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.UsedFallback);
        Assert.NotNull(body);
        Assert.Contains("\"model\":\"gpt-test\"", body, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FallbackWithoutToolModelFails()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(TestHttp.Json(HttpStatusCode.NotFound, "{}")));
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            new Uri("https://api.openai.com/v1", UriKind.Absolute),
            "sk-test",
            [TestCatalog.Model("text-only", toolCall: false)],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("tool calling", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorBodyIsShownVerbatim()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                TestHttp.Json(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}""")));
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            new Uri("https://api.openai.com/v1", UriKind.Absolute),
            "sk-test",
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("401", result.Error, StringComparison.Ordinal);
        Assert.Contains("bad key", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutFailsWithProviderMessage()
    {
        using var testerHandler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                return TestHttp.Json(HttpStatusCode.OK, "{}");
            });
        using var tester = CreateTester(testerHandler, TimeSpan.FromMilliseconds(50));

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            new Uri("https://api.openai.com/v1", UriKind.Absolute),
            "sk-test",
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("did not respond", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedModelListFails()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(TestHttp.Json(HttpStatusCode.OK, "not json")));
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            new Uri("https://api.openai.com/v1", UriKind.Absolute),
            "sk-test",
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("could not be read", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootArrayCountsModels()
    {
        using var testerHandler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(TestHttp.Json(HttpStatusCode.OK, """[{"id":"a"}]""")));
        using var tester = CreateTester(testerHandler);

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            new Uri("https://api.openai.com/v1", UriKind.Absolute),
            "sk-test",
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModelCount);
    }

    private static ProviderTester CreateTester(
        StubHttpMessageHandler handler,
        TimeSpan? timeout = null)
    {
        return new ProviderTester(handler, timeout);
    }
}
