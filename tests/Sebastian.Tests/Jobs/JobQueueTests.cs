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
            Assert.True(queue.TryAdd(job));
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

        var failed = new TestJob("fail", _ => throw new InvalidOperationException("boom"));
        var completed = new TestJob("ok", _ => Task.CompletedTask);

        Assert.True(queue.TryAdd(failed));
        Assert.True(queue.TryAdd(completed));

        await failed.Completion;
        await completed.Completion;

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RejectsDuplicateKeyWhileJobRuns()
    {
        using var gate = new SemaphoreSlim(0);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var queue = new JobQueue(services, NullLogger<JobQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = new TestJob("same", async cancellationToken =>
        {
            started.SetResult();
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        });

        Assert.True(queue.TryAdd(running));
        await started.Task;
        Assert.False(queue.TryAdd(new TestJob("same", _ => Task.CompletedTask)));

        gate.Release();
        await running.Completion;

        var again = new TestJob("same", _ => Task.CompletedTask);
        Assert.True(SpinWait.SpinUntil(() => queue.TryAdd(again), TimeSpan.FromSeconds(10)));
        await again.Completion;

        await queue.StopAsync(CancellationToken.None);
    }

    private sealed class TestJob(string key, Func<CancellationToken, Task> run) : IJob
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Key => key;

        public Task Completion => _completion.Task;

        public async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            try
            {
                await run(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _completion.TrySetResult();
            }
        }
    }
}
