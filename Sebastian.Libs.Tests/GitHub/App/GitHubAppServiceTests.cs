using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.Tests.GitHub.Personal;

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
        using var service = CreateService(
            (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(request.RequestUri!.AbsolutePath switch
                {
                    "/app" => Json(HttpStatusCode.OK, AppBody),
                    "/app/installations" => Json(HttpStatusCode.OK, InstallationsBody),
                    "/app/installations/42/access_tokens" => Json(HttpStatusCode.OK, TokenBody),
                    "/installation/repositories" => Json(HttpStatusCode.OK, RepositoriesBody),
                    var path => throw new InvalidOperationException($"Unexpected request: {path}"),
                });
            });

        var result = await service.ValidateAsync(
            Credentials(),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Sebastian", result.AppName);
        Assert.Equal("sebastian", result.AppSlug);
        Assert.Equal("octocat", result.InstallationLogin);
        Assert.Equal(7, result.RepositoryCount);
        Assert.Equal("https://github.com/apps/sebastian/installations/new", result.InstallUrl);

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
        using var service = CreateService(
            (request, _) => Task.FromResult(
                request.RequestUri!.AbsolutePath switch
                {
                    "/app" => Json(HttpStatusCode.OK, AppBody),
                    "/app/installations" => Json(
                        HttpStatusCode.OK,
                        """[{"id":7,"account":{"login":"other"}}]"""),
                    var path => throw new InvalidOperationException($"Unexpected request: {path}"),
                }));

        var result = await service.ValidateAsync(
            Credentials(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not listed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedAppReturnsFailure()
    {
        using var service = CreateService(
            (_, _) => Task.FromResult(
                Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""")));

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
        using var service = CreateService(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Json(HttpStatusCode.OK, AppBody));
            });

        var result = await service.ValidateAsync(
            Credentials() with { PrivateKey = "not a pem" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task TimeoutFailsWithProviderMessage()
    {
        using var service = CreateService(
            async (_, cancellationToken) =>
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                return Json(HttpStatusCode.OK, AppBody);
            },
            TimeSpan.FromMilliseconds(50));

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
            string.Empty);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The service owns and disposes the handler.")]
    private static GitHubAppService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TimeSpan? timeout = null)
    {
        return new GitHubAppService(
            new StubHttpMessageHandler(handler),
            timeout ?? TimeSpan.FromSeconds(5));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
