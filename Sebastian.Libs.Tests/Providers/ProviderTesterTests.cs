using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Sebastian.Libs.Providers;
using Sebastian.Libs.Tests.GitHub.Personal;

namespace Sebastian.Libs.Tests.Providers;

public sealed class ProviderTesterTests
{
    [Fact]
    public async Task OpenAiModelListReturnsCount()
    {
        using var tester = CreateTester(
            (request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal(
                    "https://api.openai.com/v1/models",
                    request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("sk-test", request.Headers.Authorization?.Parameter);
                return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[{"id":"a"},{"id":"b"}]}"""));
            });

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            "https://api.openai.com/v1",
            Credentials("sk-test"),
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.ModelCount);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task AnthropicModelListUsesAnthropicHeaders()
    {
        using var tester = CreateTester(
            (request, _) =>
            {
                Assert.Equal(
                    "https://api.anthropic.com/v1/models",
                    request.RequestUri!.AbsoluteUri);
                Assert.True(request.Headers.TryGetValues("x-api-key", out var keys));
                Assert.Equal("sk-ant", keys.Single());
                Assert.True(request.Headers.TryGetValues("anthropic-version", out var versions));
                Assert.Equal("2023-06-01", versions.Single());
                return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[{"id":"claude"}]}"""));
            });

        var result = await tester.TestAsync(
            ProviderAdapter.Anthropic,
            "https://api.anthropic.com",
            Credentials("sk-ant"),
            [TestCatalog.Model("claude")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModelCount);
    }

    [Fact]
    public async Task AnthropicBaseEndingInV1DoesNotDoubleThePath()
    {
        using var tester = CreateTester(
            (request, _) =>
            {
                Assert.Equal(
                    "https://api.anthropic.com/v1/models",
                    request.RequestUri!.AbsoluteUri);
                return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[]}"""));
            });

        var result = await tester.TestAsync(
            ProviderAdapter.Anthropic,
            "https://api.anthropic.com/v1",
            Credentials("sk-ant"),
            [TestCatalog.Model("claude")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ModelCount);
    }

    [Fact]
    public async Task AzureModelListUsesApiKeyHeader()
    {
        using var tester = CreateTester(
            (request, _) =>
            {
                Assert.Equal(
                    "https://my-resource.openai.azure.com/openai/v1/models",
                    request.RequestUri!.AbsoluteUri);
                Assert.True(request.Headers.TryGetValues("api-key", out var keys));
                Assert.Equal("azure-key", keys.Single());
                Assert.Null(request.Headers.Authorization);
                return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[{"id":"deployment"}]}"""));
            });

        var result = await tester.TestAsync(
            ProviderAdapter.Azure,
            "https://my-resource.openai.azure.com/openai/v1",
            Credentials("azure-key"),
            [TestCatalog.Model("deployment")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModelCount);
    }

    [Fact]
    public async Task Azure404DoesNotFallBackToChat()
    {
        var calls = 0;
        using var tester = CreateTester(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Json(HttpStatusCode.NotFound, """{"error":"not found"}"""));
            });

        var result = await tester.TestAsync(
            ProviderAdapter.Azure,
            "https://my-resource.openai.azure.com/openai/v1",
            Credentials("azure-key"),
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
        using var tester = CreateTester(
            async (request, cancellationToken) =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    return Json(HttpStatusCode.NotFound, """{"error":"no models endpoint"}""");
                }

                Assert.Equal(
                    "https://api.openai.com/v1/chat/completions",
                    request.RequestUri!.AbsoluteUri);
                body = await request.Content!
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                return Json(HttpStatusCode.OK, "{}");
            });

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            "https://api.openai.com/v1",
            Credentials("sk-test"),
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
        using var tester = CreateTester(
            (_, _) => Task.FromResult(Json(HttpStatusCode.NotFound, "{}")));

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            "https://api.openai.com/v1",
            Credentials("sk-test"),
            [TestCatalog.Model("text-only", toolCall: false)],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("tool calling", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorBodyIsShownVerbatim()
    {
        using var tester = CreateTester(
            (_, _) => Task.FromResult(
                Json(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}""")));

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            "https://api.openai.com/v1",
            Credentials("sk-test"),
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("401", result.Error, StringComparison.Ordinal);
        Assert.Contains("bad key", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutFailsWithProviderMessage()
    {
        using var tester = CreateTester(
            async (_, cancellationToken) =>
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                return Json(HttpStatusCode.OK, "{}");
            },
            TimeSpan.FromMilliseconds(50));

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            "https://api.openai.com/v1",
            Credentials("sk-test"),
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("did not respond", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedModelListFails()
    {
        using var tester = CreateTester(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, "not json")));

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            "https://api.openai.com/v1",
            Credentials("sk-test"),
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("could not be read", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootArrayCountsModels()
    {
        using var tester = CreateTester(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """[{"id":"a"}]""")));

        var result = await tester.TestAsync(
            ProviderAdapter.OpenAiCompatible,
            "https://api.openai.com/v1",
            Credentials("sk-test"),
            [TestCatalog.Model("gpt-test")],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModelCount);
    }

    private static ProviderCredentials Credentials(string key)
    {
        return new ProviderCredentials(
            key,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The tester owns and disposes the handler.")]
    private static ProviderTester CreateTester(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TimeSpan? timeout = null)
    {
        return new ProviderTester(new StubHttpMessageHandler(handler), timeout);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
