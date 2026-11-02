using System.Globalization;
using GitHub;
using GitHub.Octokit.Client;
using GitHub.Octokit.Client.Authentication;
using Microsoft.Kiota.Abstractions;
using GitHubAccessTokensBody =
    global::GitHub.App.Installations.Item.Access_tokens.Access_tokensPostRequestBody;

namespace Sebastian.Libs.GitHub.App;

public sealed class GitHubAppService(
    HttpMessageHandler? finalHandler = null,
    TimeSpan? timeout = null,
    TimeProvider? timeProvider = null) : IGitHubAppService, IDisposable
{
    private readonly HttpClient _httpClient = GitHubApi.CreateClient(
        finalHandler,
        timeout ?? TimeSpan.FromSeconds(30));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<GitHubAppValidation> ValidateAsync(
        GitHubAppCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var appId = credentials.AppId.Trim();
        if (ParseCredentials(credentials, out var installationId) is { } invalid)
        {
            return invalid;
        }

        using var privateKey = GitHubPrivateKey.TryCreate(credentials.PrivateKey);
        if (privateKey is null)
        {
            return GitHubAppValidation.Failed(
                "That does not look like a PEM private key. Use PKCS#8 or PKCS#1.");
        }

        try
        {
            var tokenProvider = new GitHubAppTokenProvider();
            var descriptor = tokenProvider.CreateTokenDescriptor(
                privateKey,
                appId,
                _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1));
            var jwt = tokenProvider.CreateJsonWebToken(descriptor);

            using var appAdapter = RequestAdapter.Create(
                new TokenAuthProvider(new TokenProvider(jwt)),
                _httpClient);
            var appClient = new GitHubClient(appAdapter);

            var app = await appClient.App
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (app is null || string.IsNullOrWhiteSpace(app.Name))
            {
                return GitHubAppValidation.Failed(
                    "GitHub did not return the app details.");
            }

            var slug = app.Slug ?? string.Empty;
            if (string.IsNullOrWhiteSpace(slug))
            {
                return GitHubAppValidation.Failed(
                    "GitHub returned an app without a slug.");
            }

            var installations = await appClient.App.Installations
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var installation = installations?
                .FirstOrDefault(candidate => candidate.Id == installationId);
            if (installation is null)
            {
                var known = DescribeInstallations(
                    installations?.Select(candidate => candidate.Account?.SimpleUser?.Login));
                return GitHubAppValidation.Failed(
                    $"Installation ID {installationId.ToString(CultureInfo.InvariantCulture)} is not listed for this app. Known installations: {known}.");
            }

            var installationToken = await appClient.App.Installations[installationId]
                .Access_tokens.PostAsync(
                    new GitHubAccessTokensBody(),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (installationToken?.Token is not { Length: > 0 } token)
            {
                return GitHubAppValidation.Failed(
                    "GitHub did not return an installation token.");
            }

            using var installationAdapter = RequestAdapter.Create(
                new TokenAuthProvider(new TokenProvider(token)),
                _httpClient);
            var installationClient = new GitHubClient(installationAdapter);
            var repositories = await installationClient.Installation.Repositories
                .GetAsync(
                    configuration => configuration.QueryParameters.PerPage = 1,
                    cancellationToken)
                .ConfigureAwait(false);
            var repositoryCount = repositories?.TotalCount ?? 0;
            var login = FirstNonEmpty(
                installation.Account?.SimpleUser?.Login,
                installation.Account?.Enterprise?.Slug,
                "unknown");

            return GitHubAppValidation.Passed(
                app.Name,
                slug,
                login,
                repositoryCount,
                new Uri($"https://github.com/apps/{slug}/installations/new", UriKind.Absolute));
        }
        catch (ApiException exception)
        {
            return GitHubAppValidation.Failed(exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var seconds = _httpClient.Timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            return GitHubAppValidation.Failed($"GitHub did not respond within {seconds} seconds.");
        }
        catch (HttpRequestException exception)
        {
            return GitHubAppValidation.Failed($"Could not reach GitHub: {exception.Message}");
        }
    }

    private static GitHubAppValidation? ParseCredentials(
        GitHubAppCredentials credentials,
        out int installationId)
    {
        if (!long.TryParse(
                credentials.AppId.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var appId) ||
            appId <= 0)
        {
            installationId = 0;
            return GitHubAppValidation.Failed("Enter a numeric App ID.");
        }

        return !int.TryParse(
                credentials.InstallationId.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out installationId) ||
            installationId <= 0
            ? GitHubAppValidation.Failed("Enter a numeric installation ID.")
            : null;
    }

    private static string DescribeInstallations(IEnumerable<string?>? logins)
    {
        var known = logins?
            .Where(login => !string.IsNullOrWhiteSpace(login))
            .ToArray() ?? [];
        return known.Length == 0 ? "none" : string.Join(", ", known);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return Array.Find(values, value => !string.IsNullOrWhiteSpace(value)) ?? "unknown";
    }
}
