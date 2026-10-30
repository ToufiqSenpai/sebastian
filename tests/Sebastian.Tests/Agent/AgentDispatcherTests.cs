namespace Sebastian.Tests.Agent;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Sebastian.Agent;

public sealed class AgentDispatcherTests
{
    [Fact]
    public async Task StartsJobsInParallelAndDeduplicatesByIssue()
    {
        var runner = new FakeRunner();
        using var dispatcher = new AgentDispatcher(
            runner.RunAsync,
            NullLogger<AgentDispatcher>.Instance);

        var first = new ImplementationJob("owner", "repo", 1, "First", "Body");
        var second = first with { IssueNumber = 2 };

        Assert.True(dispatcher.TryStart(first));
        Assert.False(dispatcher.TryStart(first));
        Assert.True(dispatcher.TryStart(second));
        Assert.Equal(2, runner.Calls);

        runner.Complete(first);
        Assert.True(SpinWait.SpinUntil(() => dispatcher.ActiveCount == 1, TimeSpan.FromSeconds(2)));

        Assert.True(dispatcher.TryStart(first));
        Assert.Equal(3, runner.Calls);

        runner.Complete(first);
        runner.Complete(second);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(0, dispatcher.ActiveCount);
    }

    private sealed class FakeRunner
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _completions = new();

        public int Calls;

        public Task RunAsync(ImplementationJob job, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref Calls);
            return _completions
                .GetOrAdd(job.Key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
                .Task;
        }

        public void Complete(ImplementationJob job)
        {
            if (_completions.TryRemove(job.Key, out var completion))
            {
                completion.SetResult();
            }
        }
    }
}
