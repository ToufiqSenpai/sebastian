namespace Sebastian.Agent;

internal sealed record ImplementationJob(
    string Owner,
    string Repository,
    int IssueNumber,
    string Title,
    string Body)
{
    public string Key => $"{Owner}/{Repository}#{IssueNumber}";
}
