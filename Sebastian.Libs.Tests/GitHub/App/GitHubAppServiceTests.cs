using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Sebastian.Libs.GitHub.App;

namespace Sebastian.Libs.Tests.GitHub.App;

public sealed class GitHubAppServiceTests
{
    private const string AppBody = /*lang=json,strict*/ """{"name":"Sebastian","slug":"sebastian"}""";
    private const string InstallationsBody = /*lang=json,strict*/ """[{"id":42,"account":{"login":"octocat"}}]""";
    private const string TokenBody = /*lang=json,strict*/ """{"token":"ghs_test"}""";
    private const string RepositoriesBody = /*lang=json,strict*/ """{"total_count":7,"repositories":[]}""";

    [Fact]
    public async Task ValidAppReturnsNameLoginAndRepositoryCount()
    {
        var requests = new List<HttpRequestMessage>();
        using var serviceHandler = new StubHttpMessageHandler(
            (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(request.RequestUri!.AbsolutePath switch
                {
                    "/app" => TestHttp.Json(HttpStatusCode.OK, AppBody),
                    "/app/installations" => TestHttp.Json(HttpStatusCode.OK, InstallationsBody),
                    "/app/installations/42/access_tokens" => TestHttp.Json(HttpStatusCode.OK, TokenBody),
                    "/installation/repositories" => TestHttp.Json(HttpStatusCode.OK, RepositoriesBody),
                    var path => throw new InvalidOperationException($"Unexpected request: {path}"),
                });
            });
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateAsync(
            Credentials(),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Sebastian", result.AppName);
        Assert.Equal("sebastian", result.AppSlug);
        Assert.Equal("octocat", result.InstallationLogin);
        Assert.Equal(7, result.RepositoryCount);
        Assert.Equal(
            new Uri("https://github.com/apps/sebastian/installations/new", UriKind.Absolute),
            result.InstallUrl);

        var appRequests = requests
            .Where(request => !request.RequestUri!.AbsolutePath.StartsWith(
                "/installation/",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(3, appRequests.Length);
        foreach (var request in appRequests)
        {
            var token = request.Headers.Authorization?.Parameter;
            Assert.NotNull(token);
            var parts = token.Split('.');
            Assert.Equal(3, parts.Length);
            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            Assert.Equal("123", payload.RootElement.GetProperty("iss").GetString());
            Assert.True(
                payload.RootElement.GetProperty("exp").GetInt64() >
                payload.RootElement.GetProperty("iat").GetInt64());
        }

        var repositoriesRequest = Assert.Single(
            requests,
            request => request.RequestUri!.AbsolutePath == "/installation/repositories");
        Assert.Equal("ghs_test", repositoriesRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task InstallationMustBelongToTheApp()
    {
        using var serviceHandler = new StubHttpMessageHandler(
            (request, _) => Task.FromResult(
                request.RequestUri!.AbsolutePath switch
                {
                    "/app" => TestHttp.Json(HttpStatusCode.OK, AppBody),
                    "/app/installations" => TestHttp.Json(
                        HttpStatusCode.OK,
                        """[{"id":7,"account":{"login":"other"}}]"""),
                    var path => throw new InvalidOperationException($"Unexpected request: {path}"),
                }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateAsync(
            Credentials(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not listed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedAppReturnsFailure()
    {
        using var serviceHandler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                TestHttp.Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""")));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateAsync(
            Credentials(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InvalidPrivateKeyFailsBeforeCallingGitHub()
    {
        var calls = 0;
        using var serviceHandler = new StubHttpMessageHandler(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(TestHttp.Json(HttpStatusCode.OK, AppBody));
            });
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateAsync(
            Credentials() with { PrivateKey = "not a pem" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task TimeoutFailsWithProviderMessage()
    {
        using var serviceHandler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                return TestHttp.Json(HttpStatusCode.OK, AppBody);
            });
        using var service = CreateService(serviceHandler, TimeSpan.FromMilliseconds(50));

        var result = await service.ValidateAsync(
            Credentials(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("did not respond", result.Error, StringComparison.Ordinal);
    }

    private static GitHubAppCredentials Credentials()
    {
        using var rsa = RSA.Create(2048);
        return new GitHubAppCredentials(
            "123",
            "42",
            rsa.ExportRSAPrivateKeyPem(),
            "webhook-secret-value",
            WebhookUrl: null);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');
        normalized = normalized.PadRight(
            normalized.Length + ((4 - (normalized.Length % 4)) % 4),
            '=');
        return Convert.FromBase64String(normalized);
    }

    private static GitHubAppService CreateService(
        StubHttpMessageHandler handler,
        TimeSpan? timeout = null)
    {
        return new GitHubAppService(
            handler,
            timeout ?? TimeSpan.FromSeconds(5));
    }
}
