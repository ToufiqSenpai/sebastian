using System.Diagnostics.CodeAnalysis;
using Sebastian.Libs.GitHub.Personal;
using Sebastian.Libs.Settings;

namespace Sebastian.Windows.Setup;

[SuppressMessage("Performance", "CA1812", Justification = "Created by the DI container.")]
internal sealed class SetupService
{
    private readonly KeyValueStore _store;
    private readonly IGitHubPersonalService _github;

    public SetupService(KeyValueStore store, IGitHubPersonalService github)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(github);

        _store = store;
        _github = github;
    }

    public bool IsPatConfigured => _store.Has("github:pat");

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
}
