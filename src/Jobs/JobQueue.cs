namespace Sebastian.Jobs;

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed partial class JobQueue(
    IServiceProvider services,
    ILogger<JobQueue> logger) : BackgroundService
{
    private const int MaxConcurrentJobs = 10;

    private readonly Channel<(IJob Job, TaskCompletionSource Done)> _channel =
        Channel.CreateUnbounded<(IJob Job, TaskCompletionSource Done)>();

    public Task Add(IJob job)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_channel.Writer.TryWrite((job, done)))
        {
            done.SetException(new InvalidOperationException("Job queue is not accepting jobs."));
        }

        return done.Task;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed job must not tear down the queue.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                _channel.Reader.ReadAllAsync(stoppingToken),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentJobs,
                    CancellationToken = stoppingToken,
                },
                async (item, cancellationToken) =>
                {
                    try
                    {
                        var scope = services.CreateAsyncScope();
                        await using (scope.ConfigureAwait(false))
                        {
                            await item.Job.RunAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        LogFailed(exception, item.Job.Key);
                    }
                    finally
                    {
                        item.Done.TrySetResult();
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Key} failed")]
    private partial void LogFailed(Exception exception, string key);
}
