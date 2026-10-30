using Microsoft.Extensions.Options;
using Octokit.Webhooks;
using Octokit.Webhooks.AspNetCore;
using Sebastian.Agent;
using Sebastian.Configuration;
using Sebastian.GitHub;
using Sebastian.Jobs;
using Sebastian.Libs;
using Sebastian.Webhooks;

var builder = WebApplication.CreateBuilder(args);

// appsettings.json stores {env:NAME} placeholders so secrets stay out of git.
builder.Configuration.ExpandEnvironmentPlaceholders();

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
builder.Services.AddHostedService(services => services.GetRequiredService<Git>());
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddHostedService(services => services.GetRequiredService<JobQueue>());

// Factory, not AddSingleton<TService, TImpl>: internal processor is unknown to DI's reflection path.
builder.Services.AddSingleton<WebhookEventProcessor>(
    services => new GitHubWebhookProcessor(
        services.GetRequiredService<ILogger<GitHubWebhookProcessor>>(),
        services.GetRequiredService<GitHubService>(),
        services.GetRequiredService<JobQueue>(),
        services.GetRequiredService<Git>()));

var app = builder.Build();

var config = app.Services.GetRequiredService<IOptions<SebastianConfig>>().Value;

// Construct the services now so missing credentials fail at startup.
_ = app.Services.GetRequiredService<GitHubService>();
_ = app.Services.GetRequiredService<ProviderResolver>();

app.MapGet("/health", () => "ok");
app.MapGitHubWebhooks(secret: config.GitHub.WebhookSecret);

await app.RunAsync().ConfigureAwait(false);
