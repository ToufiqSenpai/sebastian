using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.Tests.GitHub.Personal;

namespace Sebastian.Libs.Tests.Catalog;

public sealed class CatalogServiceTests : IDisposable
{
    private const string CatalogJson = /*lang=json,strict*/ """
        {
          "openai": {
            "id": "openai",
            "name": "OpenAI",
            "api": null,
            "env": ["OPENAI_API_KEY"],
            "npm": "@ai-sdk/openai",
            "doc": "https://platform.openai.com/docs/models",
            "models": {
              "gpt-test": {
                "id": "gpt-test",
                "name": "GPT Test",
                "family": "gpt",
                "attachment": true,
                "reasoning": false,
                "tool_call": true,
                "temperature": true,
                "knowledge": "2025-01",
                "release_date": "2025-01-01",
                "last_updated": "2025-01-01",
                "open_weights": false,
                "limit": { "context": 128000, "output": 8192 },
                "cost": { "input": 0.1, "output": 0.2, "cache_read": 0.01 },
                "modalities": { "input": ["text"], "output": ["text"] }
              }
            }
          },
          "anthropic": {
            "id": "anthropic",
            "name": "Anthropic",
            "api": null,
            "env": ["ANTHROPIC_API_KEY"],
            "npm": "@ai-sdk/anthropic",
            "doc": "https://docs.anthropic.com",
            "models": {}
          }
        }
        """;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sebastian-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadsParsesAndCachesCatalog()
    {
        var time = new TestTimeProvider(Now);
        using var service = CreateService(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, CatalogJson, etag: "W/\"weak\"")),
            time);

        var catalog = await service.GetAsync(CancellationToken.None);

        Assert.Equal(2, catalog.Providers.Count);
        var openai = Assert.Single(catalog.Providers, provider => provider.Id == "openai");
        Assert.Equal("OpenAI", openai.Name);
        Assert.Equal("OPENAI_API_KEY", Assert.Single(openai.Env));
        var model = Assert.Single(openai.Models);
        Assert.True(model.ToolCall);
        Assert.Equal(128000, model.ContextLimit);
        Assert.Equal(0.1m, model.InputCost);

        Assert.True(File.Exists(Path.Combine(_directory, "models.json")));
        using var meta = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(_directory, "models.meta.json"),
                CancellationToken.None));
        Assert.Equal("W/\"weak\"", meta.RootElement.GetProperty("Etag").GetString());
        Assert.Equal(Now, catalog.CheckedAt);
    }

    [Fact]
    public async Task NotModifiedKeepsCacheAndUpdatesCheckedAt()
    {
        var time = new TestTimeProvider(Now);
        using (var first = CreateService(
                   (_, _) => Task.FromResult(Json(HttpStatusCode.OK, CatalogJson, etag: "W/\"weak\"")),
                   time))
        {
            _ = await first.GetAsync(CancellationToken.None);
        }

        time.Now = Now.AddHours(1);
        var ifNoneMatch = string.Empty;
        using var second = CreateService(
            (request, _) =>
            {
                if (request.Headers.TryGetValues("If-None-Match", out var values))
                {
                    ifNoneMatch = values.Single();
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            },
            time);

        var catalog = await second.GetAsync(CancellationToken.None);

        Assert.Equal("W/\"weak\"", ifNoneMatch);
        Assert.Equal(2, catalog.Providers.Count);
        Assert.Equal(time.Now, catalog.CheckedAt);
        Assert.Null(catalog.Warning);
    }

    [Fact]
    public async Task FailedRefreshReturnsStaleCache()
    {
        var time = new TestTimeProvider(Now);
        using (var first = CreateService(
                   (_, _) => Task.FromResult(Json(HttpStatusCode.OK, CatalogJson, etag: "\"abc\"")),
                   time))
        {
            _ = await first.GetAsync(CancellationToken.None);
        }

        using var second = CreateService(
            (_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, "boom")),
            time);

        var catalog = await second.GetAsync(CancellationToken.None);

        Assert.Equal(2, catalog.Providers.Count);
        Assert.NotNull(catalog.Warning);
        Assert.Contains("Using cached catalog", catalog.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsWhenThereIsNoCache()
    {
        var time = new TestTimeProvider(Now);
        using var service = CreateService(
            (_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, "boom")),
            time);

        _ = await Assert.ThrowsAsync<CatalogException>(
            () => service.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CorruptCacheWithFailedRefreshFails()
    {
        _ = Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "models.json"),
            "not json",
            CancellationToken.None);

        var time = new TestTimeProvider(Now);
        using var service = CreateService(
            (_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, "boom")),
            time);

        _ = await Assert.ThrowsAsync<CatalogException>(
            () => service.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CorruptCacheIsReplacedBySuccessfulRefresh()
    {
        _ = Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "models.json"),
            "not json",
            CancellationToken.None);

        var time = new TestTimeProvider(Now);
        using var service = CreateService(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, CatalogJson)),
            time);

        var catalog = await service.GetAsync(CancellationToken.None);

        Assert.Equal(2, catalog.Providers.Count);
        var cached = await File.ReadAllTextAsync(
            Path.Combine(_directory, "models.json"),
            CancellationToken.None);
        Assert.Contains("openai", cached, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizeResponseIsRejected()
    {
        var time = new TestTimeProvider(Now);
        using var service = CreateService(
            (_, _) =>
            {
                var content = new ByteArrayContent([1]);
                content.Headers.ContentLength = (16L * 1024 * 1024) + 1;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
            },
            time);

        _ = await Assert.ThrowsAsync<CatalogException>(
            () => service.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NoEtagUsesCacheForTwentyFourHours()
    {
        var time = new TestTimeProvider(Now);
        using (var first = CreateService(
                   (_, _) => Task.FromResult(Json(HttpStatusCode.OK, CatalogJson)),
                   time))
        {
            _ = await first.GetAsync(CancellationToken.None);
        }

        time.Now = Now.AddHours(23);
        var dayRequests = 0;
        using (var second = CreateService(
                   (_, _) =>
                   {
                       dayRequests++;
                       return Task.FromResult(Json(HttpStatusCode.OK, CatalogJson));
                   },
                   time))
        {
            var catalog = await second.GetAsync(CancellationToken.None);
            Assert.Equal(2, catalog.Providers.Count);
        }

        Assert.Equal(0, dayRequests);

        time.Now = Now.AddHours(25);
        var nextDayRequests = 0;
        using var third = CreateService(
            (_, _) =>
            {
                nextDayRequests++;
                return Task.FromResult(Json(HttpStatusCode.OK, CatalogJson));
            },
            time);
        _ = await third.GetAsync(CancellationToken.None);

        Assert.Equal(1, nextDayRequests);
    }

    [Fact]
    public async Task ConcurrentCallsShareOneRefresh()
    {
        var time = new TestTimeProvider(Now);
        var calls = 0;
        using var service = CreateService(
            async (_, _) =>
            {
                _ = Interlocked.Increment(ref calls);
                await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
                return Json(HttpStatusCode.OK, CatalogJson);
            },
            time);

        var first = service.GetAsync(CancellationToken.None);
        var second = service.GetAsync(CancellationToken.None);
        _ = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DateTimeOffset Now { get; } = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The service under test owns and disposes the handler.")]
    private CatalogService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TestTimeProvider time)
    {
        return new CatalogService(
            new StubHttpMessageHandler(handler),
            _directory,
            TimeSpan.FromSeconds(5),
            time);
    }

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body,
        string? etag = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (etag is not null)
        {
            _ = response.Headers.TryAddWithoutValidation("ETag", etag);
        }

        return response;
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
