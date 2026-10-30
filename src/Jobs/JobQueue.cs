namespace Sebastian.Jobs;

using System.Collections.Concurrent;
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

    private readonly Channel<IJob> _channel = Channel.CreateUnbounded<IJob>();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(IJob job)
    {
        if (!_inFlight.TryAdd(job.Key, 0))
        {
            return false;
        }

        if (!_channel.Writer.TryWrite(job))
        {
            _inFlight.TryRemove(job.Key, out _);
            return false;
        }

        return true;
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
                async (job, cancellationToken) =>
                {
                    try
                    {
                        var scope = services.CreateAsyncScope();
                        await using (scope.ConfigureAwait(false))
                        {
                            await job.RunAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        LogFailed(exception, job.Key);
                    }
                    finally
                    {
                        _inFlight.TryRemove(job.Key, out _);
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
