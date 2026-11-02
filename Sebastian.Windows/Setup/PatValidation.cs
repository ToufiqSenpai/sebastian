namespace Sebastian.Windows.Setup;

internal sealed record PatValidation(
    string Login,
    int RepositoryCount,
    IReadOnlyList<string> Scopes);
