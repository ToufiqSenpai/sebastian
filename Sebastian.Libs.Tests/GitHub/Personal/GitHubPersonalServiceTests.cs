using System.Net;
using System.Text;
using Sebastian.Libs.GitHub.Personal;

namespace Sebastian.Libs.Tests.GitHub.Personal;

public sealed class GitHubPersonalServiceTests
{
    private const string UserBody = /*lang=json,strict*/ """{"login":"octocat","user_view_type":"private"}""";

    [Fact]
    public async Task ValidTokenReturnsLoginRepositoryCountAndScopes()
    {
        var requests = new List<CapturedRequest>();
        using var serviceHandler = new StubHttpMessageHandler(Record(
            requests,
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(
                    HttpStatusCode.OK,
                    UserBody,
                    response => response.Headers.TryAddWithoutValidation("x-oauth-scopes", "repo, read:org")),
                "/user/repos" => Json(
                    HttpStatusCode.OK,
                                         /*lang=json,strict*/
                                         """[{"full_name":"octocat/Hello-World"}]""",
                    response => response.Headers.TryAddWithoutValidation(
                        "Link",
                        "<https://api.github.com/user/repos?per_page=1&page=7>; rel=\"last\"")),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("octocat", result.Login);
        Assert.Equal(7, result.RepositoryCount);
        Assert.Contains("repo", result.Scopes);
        Assert.Null(result.Error);
        Assert.Equal(2, requests.Count);
        Assert.All(
            requests,
            request =>
            {
                Assert.Equal("Bearer", request.AuthorizationScheme);
                Assert.Equal("ghp_test", request.AuthorizationParameter);
                Assert.Contains("Sebastian/0.1", request.UserAgent, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("2022-11-28", request.ApiVersion);
                Assert.DoesNotContain("ghp_test", request.Uri, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task RepositoryCountReadsLastPageWhenPageIsNotLastParameter()
    {
        using var serviceHandler = new StubHttpMessageHandler(Record(
            [],
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(
                    HttpStatusCode.OK,
                    UserBody,
                    response => response.Headers.TryAddWithoutValidation("x-oauth-scopes", "repo")),
                "/user/repos" => Json(
                    HttpStatusCode.OK,
                                         /*lang=json,strict*/
                                         """[{"full_name":"octocat/Hello-World"}]""",
                    response => response.Headers.TryAddWithoutValidation(
                        "Link",
                        "<https://api.github.com/user/repos?page=12&per_page=1>; rel=\"last\"")),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(12, result.RepositoryCount);
    }

    [Fact]
    public async Task MissingRepoScopeFails()
    {
        using var serviceHandler = new StubHttpMessageHandler(Record(
            [],
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(
                    HttpStatusCode.OK,
                    UserBody,
                    response => response.Headers.TryAddWithoutValidation("x-oauth-scopes", "read:org")),
                "/user/repos" => Json(HttpStatusCode.OK, "[]"),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("repo scope", result.Error, StringComparison.Ordinal);
        Assert.Equal("octocat", result.Login);
        Assert.Contains("read:org", result.Scopes);
    }

    [Fact]
    public async Task MissingScopeHeaderFailsWithClassicTokenHint()
    {
        using var serviceHandler = new StubHttpMessageHandler(Record(
            [],
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(HttpStatusCode.OK, UserBody),
                "/user/repos" => Json(HttpStatusCode.OK, "[]"),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("classic", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnauthorizedReturnsGitHubMessageAndStops()
    {
        var requests = new List<CapturedRequest>();
        using var serviceHandler = new StubHttpMessageHandler(Record(
            requests,
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(HttpStatusCode.Unauthorized, /*lang=json,strict*/ """{"message":"Bad credentials"}"""),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Bad credentials", result.Error);
        _ = Assert.Single(requests);
    }

    [Fact]
    public async Task EmptyRepositoryListWithoutLinkReturnsZero()
    {
        using var serviceHandler = new StubHttpMessageHandler(Record(
            [],
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(
                    HttpStatusCode.OK,
                    UserBody,
                    response => response.Headers.TryAddWithoutValidation("x-oauth-scopes", "repo")),
                "/user/repos" => Json(HttpStatusCode.OK, "[]"),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.RepositoryCount);
    }

    [Fact]
    public async Task SingleRepositoryWithoutLinkReturnsOne()
    {
        using var serviceHandler = new StubHttpMessageHandler(Record(
            [],
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(
                    HttpStatusCode.OK,
                    UserBody,
                    response => response.Headers.TryAddWithoutValidation("x-oauth-scopes", "repo")),
                "/user/repos" => Json(
                    HttpStatusCode.OK,
                                         /*lang=json,strict*/
                                         """[{"full_name":"octocat/Hello-World"}]"""),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.RepositoryCount);
    }

    [Fact]
    public async Task RateLimitOnRepositoryListReturnsGitHubMessage()
    {
        using var serviceHandler = new StubHttpMessageHandler(Record(
            [],
            request => request.RequestUri!.AbsolutePath switch
            {
                "/user" => Json(
                    HttpStatusCode.OK,
                    UserBody,
                    response => response.Headers.TryAddWithoutValidation("x-oauth-scopes", "repo")),
                "/user/repos" => Json(
                    HttpStatusCode.Forbidden,
                                         /*lang=json,strict*/
                                         """{"message":"API rate limit exceeded"}"""),
                var path => throw new InvalidOperationException($"Unexpected request: {path}"),
            }));
        using var service = CreateService(serviceHandler);

        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("API rate limit exceeded", result.Error);
    }

    [Fact]
    public async Task TimeoutReturnsFailure()
    {
        using var handler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var service = new GitHubPersonalService(handler, TimeSpan.FromMilliseconds(50));
        var result = await service.ValidateTokenAsync("ghp_test", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("did not respond", result.Error, StringComparison.Ordinal);
    }

    private static GitHubPersonalService CreateService(
        StubHttpMessageHandler handler,
        TimeSpan? timeout = null)
    {
        return new GitHubPersonalService(handler, timeout);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Record(
        List<CapturedRequest> requests,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return (request, _) =>
        {
            requests.Add(Capture(request));
            return Task.FromResult(responder(request));
        };
    }

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body,
        Action<HttpResponseMessage>? configure = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        configure?.Invoke(response);
        return response;
    }

    private static CapturedRequest Capture(HttpRequestMessage request)
    {
        var apiVersion = request.Headers.TryGetValues("X-GitHub-Api-Version", out var values)
            ? values.Single()
            : null;

        return new CapturedRequest(
            request.RequestUri!.ToString(),
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            request.Headers.UserAgent.ToString(),
            string.Join(", ", request.Headers.Accept.Select(accept => accept.MediaType)),
            apiVersion);
    }
}
