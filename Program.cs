using Microsoft.Extensions.Options;
using Octokit.Webhooks;
using Octokit.Webhooks.AspNetCore;
using Sebastian.Agent;
using Sebastian.Configuration;
using Sebastian.GitHub;
using Sebastian.Libs;
using Sebastian.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SebastianConfig>()
    .Bind(builder.Configuration)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SebastianConfig>>(new ValidateSebastianConfig());

builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<ProviderResolver>(
    services => new ProviderResolver(services.GetRequiredService<IOptions<SebastianConfig>>()));
builder.Services.AddSingleton<Git>(
    services => new Git(
        services.GetRequiredService<IHostEnvironment>(),
        services.GetRequiredService<ILogger<Git>>()));
builder.Services.AddSingleton<ImplementationRunner>(
    services => new ImplementationRunner(
        services.GetRequiredService<IOptions<SebastianConfig>>(),
        services.GetRequiredService<ProviderResolver>(),
        services.GetRequiredService<Git>(),
        services.GetRequiredService<ILogger<ImplementationRunner>>()));
builder.Services.AddSingleton<AgentDispatcher>(
    services => new AgentDispatcher(
        services.GetRequiredService<ImplementationRunner>().RunAsync,
        services.GetRequiredService<ILogger<AgentDispatcher>>()));
builder.Services.AddHostedService(
    services => services.GetRequiredService<AgentDispatcher>());

// Factory, not AddSingleton<TService, TImpl>: internal processor is unknown to DI's reflection path.
builder.Services.AddSingleton<WebhookEventProcessor>(
    services => new GitHubWebhookProcessor(
        services.GetRequiredService<ILogger<GitHubWebhookProcessor>>(),
        services.GetRequiredService<GitHubService>(),
        services.GetRequiredService<AgentDispatcher>()));

var app = builder.Build();

var config = app.Services.GetRequiredService<IOptions<SebastianConfig>>().Value;

// Construct the services now so missing credentials or an invalid repository fail at startup.
_ = app.Services.GetRequiredService<GitHubService>();
_ = app.Services.GetRequiredService<ProviderResolver>();
app.Services.GetRequiredService<Git>().Validate();

app.MapGet("/health", () => "ok");
app.MapGitHubWebhooks(secret: config.GitHub.WebhookSecret);

await app.RunAsync().ConfigureAwait(false);
