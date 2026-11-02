using System.Globalization;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.GitHub.Personal;
using Sebastian.Libs.Settings;

namespace Sebastian.Windows.Setup;

internal sealed class SetupService
{
    private readonly KeyValueStore _store;
    private readonly GitHubPersonalService _github;
    private readonly GitHubAppSetupService _githubApp;

    public SetupService(
        KeyValueStore store,
        GitHubPersonalService github,
        GitHubAppSetupService githubApp)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(github);
        ArgumentNullException.ThrowIfNull(githubApp);

        _store = store;
        _github = github;
        _githubApp = githubApp;
    }

    public bool IsPatConfigured => _store.Has("github:pat");

    public bool IsGitHubAppConfigured => _githubApp.IsConfigured;

    public int? CompletedStep
    {
        get
        {
            var value = _store["setup:step"];
            return int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var step) &&
                step >= 0
                ? step
                : null;
        }
    }

    public GitHubAppStoredState LoadGitHubAppState()
    {
        return _githubApp.LoadState();
    }

    public void MarkStepCompleted(int step)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        _store.Set(
            "setup:step",
            step.ToString(CultureInfo.InvariantCulture),
            isSecret: false);
    }

    public async Task<PatValidation> TestPatAsync(
        string? candidateToken,
        CancellationToken cancellationToken)
    {
        var candidate = string.IsNullOrWhiteSpace(candidateToken)
            ? null
            : candidateToken.Trim();

        var token = candidate ?? _store["github:pat"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new SetupException("Enter a personal access token.");
        }

        GitHubTokenValidation validation;
        try
        {
            validation = await _github
                .ValidateTokenAsync(token, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled: let it propagate instead of being wrapped below.
            throw;
        }
        catch (Exception exception)
        {
            throw new SetupException(
                $"Could not verify the token: {exception.Message}",
                exception);
        }

        if (!validation.Success)
        {
            throw new SetupException(validation.Error ?? "GitHub rejected the token.");
        }

        if (candidate is not null)
        {
            try
            {
                _store.Set("github:pat", candidate, isSecret: true);
            }
            catch (Exception exception)
            {
                throw new SetupException(
                    "The token was verified but could not be stored securely.",
                    exception);
            }
        }

        return new PatValidation(
            validation.Login ?? "unknown",
            validation.RepositoryCount,
            validation.Scopes);
    }

    public async Task<GitHubAppValidation> TestAndSaveGitHubAppAsync(
        GitHubAppCredentials candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        GitHubAppValidation validation;
        try
        {
            validation = await _githubApp
                .TestAndSaveAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SetupException(
                $"Could not verify the GitHub App: {exception.Message}",
                exception);
        }

        return !validation.Success
            ? throw new SetupException(
                validation.Error ?? "GitHub rejected the app credentials.")
            : validation;
    }
}
