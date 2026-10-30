namespace Sebastian.Webhooks;

using Microsoft.Extensions.Logging;
using Octokit.Webhooks;
using Octokit.Webhooks.Events;
using Octokit.Webhooks.Events.Issues;
using Octokit.Webhooks.Models;
using Sebastian.Agent;
using Sebastian.GitHub;
using Sebastian.GitHub.Issues;

internal sealed partial class GitHubWebhookProcessor(
    ILogger<GitHubWebhookProcessor> logger,
    GitHubService gitHubService,
    AgentDispatcher agentDispatcher) : WebhookEventProcessor
{
    private const string FeatureLabel = "feature";
    private const string ReadyForAgentLabel = "ready-for-agent";

    public override ValueTask ProcessWebhookAsync(
        WebhookHeaders headers,
        WebhookEvent webhookEvent,
        CancellationToken cancellationToken = default)
    {
        LogReceived(webhookEvent.GetType().Name, headers.Event);
        return base.ProcessWebhookAsync(headers, webhookEvent, cancellationToken);
    }

    protected override async ValueTask ProcessIssuesWebhookAsync(
        WebhookHeaders headers,
        IssuesEvent issuesEvent,
        IssuesAction action,
        CancellationToken cancellationToken = default)
    {
        if (action != IssuesAction.Opened &&
            action != IssuesAction.Labeled &&
            action != IssuesAction.Edited &&
            action != IssuesAction.Reopened)
        {
            return;
        }

        if (issuesEvent.Issue.State != IssueState.Open || !HasLabel(issuesEvent.Issue, FeatureLabel))
        {
            return;
        }

        var repository = issuesEvent.Repository;
        var owner = repository?.Owner?.Login;
        var name = repository?.Name;
        if (repository is null || owner is null || name is null)
        {
            return;
        }

        var repositoryName = repository.FullName ?? "unknown";
        var issueNumber = (int)issuesEvent.Issue.Number;
        var missingSections = SpecTemplateValidator.FindMissingSections(issuesEvent.Issue.Body);

        if (missingSections.Count > 0)
        {
            var sections = string.Join(", ", missingSections);
            var comment = $"Closed: the spec is missing required sections: {sections}.";

            await gitHubService.PostCommentAsync(
                owner,
                name,
                issueNumber,
                comment,
                cancellationToken).ConfigureAwait(false);
            await gitHubService.CloseIssueAsync(
                owner,
                name,
                issueNumber,
                cancellationToken).ConfigureAwait(false);

            LogInvalidSpec(repositoryName, issueNumber, sections);
            return;
        }

        if (HasLabel(issuesEvent.Issue, ReadyForAgentLabel))
        {
            var job = new ImplementationJob(
                owner,
                name,
                issueNumber,
                issuesEvent.Issue.Title ?? string.Empty,
                issuesEvent.Issue.Body ?? string.Empty);

            if (agentDispatcher.TryStart(job))
            {
                LogImplementationStarted(repositoryName, issueNumber);
            }
            else
            {
                LogImplementationAlreadyRunning(repositoryName, issueNumber);
            }
        }
    }

    private static bool HasLabel(Issue issue, string name) =>
        issue.Labels?.Any(label => string.Equals(label.Name, name, StringComparison.OrdinalIgnoreCase)) ?? false;

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {EventType} ({Event})")]
    private partial void LogReceived(string eventType, string? @event);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started implementation for {Repository}#{Number}")]
    private partial void LogImplementationStarted(string repository, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Implementation already running for {Repository}#{Number}")]
    private partial void LogImplementationAlreadyRunning(string repository, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Issue {Repository}#{Number} closed; missing spec sections: {Sections}")]
    private partial void LogInvalidSpec(string repository, int number, string sections);
}
