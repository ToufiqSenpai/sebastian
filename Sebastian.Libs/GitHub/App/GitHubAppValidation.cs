namespace Sebastian.Libs.GitHub.App;

public sealed record GitHubAppValidation(
    bool Success,
    string AppName,
    string AppSlug,
    string InstallationLogin,
    int RepositoryCount,
    string InstallUrl,
    string? Error)
{
    public static GitHubAppValidation Passed(
        string appName,
        string appSlug,
        string installationLogin,
        int repositoryCount,
        string installUrl)
    {
        return new GitHubAppValidation(
            true,
            appName,
            appSlug,
            installationLogin,
            repositoryCount,
            installUrl,
            Error: null);
    }

    public static GitHubAppValidation Failed(string error)
    {
        return new GitHubAppValidation(
            false,
            AppName: string.Empty,
            AppSlug: string.Empty,
            InstallationLogin: string.Empty,
            RepositoryCount: 0,
            InstallUrl: string.Empty,
            error);
    }
}
