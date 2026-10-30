namespace Sebastian.Jobs;

internal interface IJob
{
    string Key { get; }

    Task RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}
