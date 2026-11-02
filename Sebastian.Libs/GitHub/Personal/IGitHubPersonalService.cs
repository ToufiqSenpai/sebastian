namespace Sebastian.Libs.GitHub.Personal;

public interface IGitHubPersonalService
{
    Task<GitHubTokenValidation> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken);
}
