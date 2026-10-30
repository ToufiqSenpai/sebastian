namespace Sebastian.Agent;

#pragma warning disable OPENAI001 // OpenAI client experimental API surface.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI experimental extensions.

using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Sebastian.Configuration;

internal sealed class ProviderResolver
{
    private const string DefaultEndpoint = "https://api.openai.com/v1";

    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string _model;

    public ProviderResolver(IOptions<SebastianConfig> options)
    {
        var agent = options.Value.Agent
            ?? throw new InvalidOperationException("Agent configuration is missing.");

        if (agent.General is null)
        {
            throw new InvalidOperationException("Agent.General configuration is missing.");
        }

        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            throw new InvalidOperationException("Agent.Name is required.");
        }

        if (string.IsNullOrWhiteSpace(agent.General.Provider) || string.IsNullOrWhiteSpace(agent.General.Model))
        {
            throw new InvalidOperationException("Agent.General.Provider and Agent.General.Model are required.");
        }

        if (agent.Providers is null || agent.Providers.Count == 0)
        {
            throw new InvalidOperationException("At least one agent provider must be configured.");
        }

        var duplicate = agent.Providers
            .GroupBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Agent provider '{duplicate.Key}' is configured more than once.");
        }

        var matches = agent.Providers
            .Where(provider => string.Equals(provider.Name, agent.General.Provider, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Agent provider '{agent.General.Provider}' is not configured exactly once.");
        }

        var selected = matches[0];
        if (string.IsNullOrWhiteSpace(selected.ApiKey))
        {
            throw new InvalidOperationException($"Agent provider '{selected.Name}' has no API key.");
        }

        if (!Uri.TryCreate(selected.Endpoint ?? DefaultEndpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"Agent provider '{selected.Name}' has an invalid endpoint.");
        }

        _apiKey = selected.ApiKey;
        _endpoint = endpoint;
        _model = agent.General.Model;
    }

    public IChatClient CreateChatClient()
    {
        var client = new OpenAIClient(
            new ApiKeyCredential(_apiKey),
            new OpenAIClientOptions { Endpoint = _endpoint });

        return client.GetChatClient(_model).AsIChatClient();
    }
}
