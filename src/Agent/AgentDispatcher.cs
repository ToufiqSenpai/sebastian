namespace Sebastian.Agent;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed partial class AgentDispatcher(
    Func<ImplementationJob, CancellationToken, Task> run,
    ILogger<AgentDispatcher> logger) : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopping = new();

    internal int ActiveCount => _runs.Count;

    public bool TryStart(ImplementationJob job)
    {
        if (_stopping.IsCancellationRequested)
        {
            return false;
        }

        var lazy = new Lazy<Task>(
            () => RunAsync(job),
            LazyThreadSafetyMode.ExecutionAndPublication);

        if (!_runs.TryAdd(job.Key, lazy))
        {
            return false;
        }

        _ = lazy.Value;
        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        var tasks = _runs.Values.Select(lazy => lazy.Value).ToArray();
        try
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown timeout; cancelled runs are already logged.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed run must not tear down the dispatcher.")]
    private async Task RunAsync(ImplementationJob job)
    {
        try
        {
            LogStarted(job.Repository, job.IssueNumber);
            await run(job, _stopping.Token).ConfigureAwait(false);
            LogCompleted(job.Repository, job.IssueNumber);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            LogCancelled(job.Repository, job.IssueNumber);
        }
        catch (Exception exception)
        {
            LogFailed(exception, job.Repository, job.IssueNumber);
        }
        finally
        {
            _runs.TryRemove(job.Key, out _);
        }
    }

    public void Dispose()
    {
        _stopping.Dispose();
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting implementation for {Repository}#{IssueNumber}")]
    private partial void LogStarted(string repository, int issueNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Implementation finished for {Repository}#{IssueNumber}")]
    private partial void LogCompleted(string repository, int issueNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Implementation cancelled for {Repository}#{IssueNumber}")]
    private partial void LogCancelled(string repository, int issueNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Implementation failed for {Repository}#{IssueNumber}")]
    private partial void LogFailed(Exception exception, string repository, int issueNumber);
}
