namespace Sebastian.Libs.GitHub.App;

public interface IGitHubAppService
{
    Task<GitHubAppValidation> ValidateAsync(
        GitHubAppCredentials credentials,
        CancellationToken cancellationToken);
}
