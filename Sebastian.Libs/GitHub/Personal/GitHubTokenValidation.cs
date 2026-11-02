namespace Sebastian.Libs.GitHub.Personal;

public sealed record GitHubTokenValidation(
    bool Success,
    string? Login,
    int RepositoryCount,
    IReadOnlyList<string> Scopes,
    string? Error)
{
    public static GitHubTokenValidation Failed(string error)
    {
        return new(
            false,
            null,
            0,
            [],
            string.IsNullOrWhiteSpace(error) ? "GitHub rejected the token." : error);
    }
}
