namespace Sebastian.Tests.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sebastian.Jobs;

public sealed class JobQueueTests
{
    [Fact]
    public async Task RunsAtMostTenJobsConcurrently()
    {
        var gate = new SemaphoreSlim(0);
        var started = 0;
        var completed = 0;

        using var services = new ServiceCollection().BuildServiceProvider();
        using var queue = new JobQueue(services, NullLogger<JobQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        for (var i = 1; i <= 12; i++)
        {
            var job = new TestJob(
                $"job-{i}",
                async cancellationToken =>
                {
                    _ = Interlocked.Increment(ref started);
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    _ = Interlocked.Increment(ref completed);
                });
            _ = queue.Add(job);
        }

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) == 10, TimeSpan.FromSeconds(10)));
        Assert.Equal(10, Volatile.Read(ref started));

        gate.Release(10);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) == 12, TimeSpan.FromSeconds(10)));
        gate.Release(2);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref completed) == 12, TimeSpan.FromSeconds(10)));

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContinuesAfterJobFailure()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var queue = new JobQueue(services, NullLogger<JobQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        var failed = queue.Add(new TestJob("fail", _ => throw new InvalidOperationException("boom")));
        var completed = queue.Add(new TestJob("ok", _ => Task.CompletedTask));

        await failed;
        await completed;

        await queue.StopAsync(CancellationToken.None);
    }

    private sealed class TestJob(string key, Func<CancellationToken, Task> run) : IJob
    {
        public string Key => key;

        public Task RunAsync(IServiceProvider services, CancellationToken cancellationToken) =>
            run(cancellationToken);
    }
}
