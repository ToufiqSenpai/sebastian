namespace Sebastian.GitHub;

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Sebastian.Configuration;
using GitHubAppAuthProvider = global::GitHub.Octokit.Client.Authentication.AppInstallationAuthProvider;
using GitHubAppTokenProvider = global::GitHub.Octokit.Client.Authentication.GitHubAppTokenProvider;
using GitHubClient = global::GitHub.GitHubClient;
using GitHubHttpAdapter = global::Microsoft.Kiota.Http.HttpClientLibrary.HttpClientRequestAdapter;
using GitHubInstallationTokenProvider = global::GitHub.Octokit.Client.Authentication.AppInstallationTokenProvider;
using GitHubRequestAdapter = global::GitHub.Octokit.Client.RequestAdapter;
using IssueCommentBody = global::GitHub.Repos.Item.Item.Issues.Item.Comments.CommentsPostRequestBody;
using IssueUpdateBody = global::GitHub.Repos.Item.Item.Issues.Item.WithIssue_numberPatchRequestBody;
using IssueUpdateState = global::GitHub.Repos.Item.Item.Issues.Item.WithIssue_numberPatchRequestBody_state;
using IssueUpdateStateReason = global::GitHub.Repos.Item.Item.Issues.Item.WithIssue_numberPatchRequestBody_state_reason;

internal sealed class GitHubService : IDisposable
{
    private readonly RSA _privateKey;
    private readonly GitHubHttpAdapter _adapter;
    private readonly GitHubClient _client;

    public GitHubService(IOptions<SebastianConfig> options)
    {
        var config = options.Value.GitHub;

        _privateKey = RSA.Create();
        _privateKey.ImportFromPem(File.ReadAllText(config.PrivateKeyPath));

        var tokenProvider = new GitHubInstallationTokenProvider(
            config.AppId,
            _privateKey,
            config.InstallationId,
            new GitHubAppTokenProvider());

        _adapter = GitHubRequestAdapter.Create(new GitHubAppAuthProvider(tokenProvider));
        _client = new GitHubClient(_adapter);
    }

    public async Task PostCommentAsync(
        string owner,
        string repository,
        int issueNumber,
        string comment,
        CancellationToken cancellationToken)
    {
        var issue = _client.Repos[owner][repository].Issues[issueNumber];

        await issue.Comments.PostAsync(
            new IssueCommentBody { Body = comment },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseIssueAsync(
        string owner,
        string repository,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        var issue = _client.Repos[owner][repository].Issues[issueNumber];

        await issue.PatchAsync(
            new IssueUpdateBody
            {
                State = IssueUpdateState.Closed,
                StateReason = IssueUpdateStateReason.Not_planned,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IssueComment>> GetCommentsAsync(
        string owner,
        string repository,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        // shortcut: first 100 comments only; add a Page loop if threads get longer.
        var comments = await _client.Repos[owner][repository].Issues[issueNumber].Comments
            .GetAsync(
                config => config.QueryParameters.PerPage = 100,
                cancellationToken).ConfigureAwait(false);

        return [.. (comments ?? [])
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Body))
            .Select(comment => new IssueComment(comment.User?.Login ?? "unknown", comment.Body!))];
    }

    public void Dispose()
    {
        _adapter.Dispose();
        _privateKey.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed record IssueComment(string Author, string Body);
