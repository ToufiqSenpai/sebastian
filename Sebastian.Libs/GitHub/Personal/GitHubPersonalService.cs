using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GitHub;
using GitHub.Octokit.Client;
using GitHub.Octokit.Client.Authentication;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;

namespace Sebastian.Libs.GitHub.Personal;

public sealed partial class GitHubPersonalService : IGitHubPersonalService, IDisposable
{
    private const string BaseUrl = "https://api.github.com";
    private const string ApiVersion = "2022-11-28";
    private const string UserAgent = "Sebastian/0.1";

    private readonly MutableTokenProvider _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly HttpClientRequestAdapter _adapter;
    private readonly GitHubClient _client;

    public GitHubPersonalService(
        HttpMessageHandler? finalHandler = null,
        TimeSpan? timeout = null)
    {
        _tokenProvider = new MutableTokenProvider();

        _httpClient = KiotaClientFactory.Create(finalHandler);
        _httpClient.BaseAddress = new Uri(BaseUrl, UriKind.Absolute);
        _httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);

        _adapter = RequestAdapter.Create(
            new TokenAuthProvider(_tokenProvider),
            _httpClient);

        _client = new GitHubClient(_adapter);
    }

    public void Dispose()
    {
        _adapter.Dispose();
        _httpClient.Dispose();
    }

    // shortcut: one client with a mutable token; concurrent validations would race. The wizard serializes tests.
    public async Task<GitHubTokenValidation> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _tokenProvider.Token = token;

        try
        {
            HeadersInspectionHandlerOption userHeaders = new()
            {
                InspectResponseHeaders = true,
            };

            var user = await _client.User
                .GetAsync(
                    configuration => configuration.Options = [userHeaders],
                    cancellationToken)
                .ConfigureAwait(false);

            var scopes = ReadScopes(userHeaders);
            var login = user?.PrivateUser?.Login ?? user?.PublicUser?.Login;

            HeadersInspectionHandlerOption repositoryHeaders = new()
            {
                InspectResponseHeaders = true,
            };

            var repositories = await _client.User.Repos
                .GetAsync(
                    configuration =>
                    {
                        configuration.QueryParameters.PerPage = 1;
                        configuration.Options = [repositoryHeaders];
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var repositoryCount = ReadRepositoryCount(
                repositoryHeaders,
                repositories?.Count ?? 0);

            return !scopes.Contains("repo", StringComparer.OrdinalIgnoreCase)
                ? new GitHubTokenValidation(
                    Success: false,
                    Login: login,
                    RepositoryCount: repositoryCount,
                    Scopes: scopes,
                    Error: "The token does not report the repo scope. Use a classic personal access token with the repo scope.")
                : new GitHubTokenValidation(
                Success: true,
                Login: login,
                RepositoryCount: repositoryCount,
                Scopes: scopes,
                Error: null);
        }
        catch (ApiException exception)
        {
            return GitHubTokenValidation.Failed(exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GitHubTokenValidation.Failed("GitHub did not respond within 30 seconds.");
        }
        catch (HttpRequestException exception)
        {
            return GitHubTokenValidation.Failed($"Could not reach GitHub: {exception.Message}");
        }
        catch (JsonException)
        {
            return GitHubTokenValidation.Failed("GitHub returned a response that could not be read.");
        }
    }

    private static string[] ReadScopes(HeadersInspectionHandlerOption headers)
    {
        return !headers.ResponseHeaders.TryGetValue("x-oauth-scopes", out var values)
            ? []
            : [
            .. values
                .SelectMany(value => value.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static int ReadRepositoryCount(
        HeadersInspectionHandlerOption headers,
        int fallback)
    {
        if (!headers.ResponseHeaders.TryGetValue("Link", out var values))
        {
            return fallback;
        }

        var match = LastPageRegex().Match(string.Join(',', values));
        return match.Success &&
               int.TryParse(
                   match.Groups[1].Value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var page)
            ? page
            : fallback;
    }

    [GeneratedRegex("[?&]page=(\\d+)[^>]*>;\\s*rel=\"last\"", RegexOptions.CultureInvariant)]
    private static partial Regex LastPageRegex();

    // The SDK keeps the token provider, not the token, so one client can validate a candidate token.
    private sealed class MutableTokenProvider : IAccessTokenProvider
    {
        public string Token { get; set; } = string.Empty;

        public AllowedHostsValidator AllowedHostsValidator { get; } = new();

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = default,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Token);
        }
    }
}
