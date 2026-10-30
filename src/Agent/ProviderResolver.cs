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
        var agent = options.Value.Agent;
        var selected = agent.Providers.Single(provider =>
            string.Equals(provider.Name, agent.General.Provider, StringComparison.OrdinalIgnoreCase));

        _apiKey = selected.ApiKey;
        _endpoint = new Uri(selected.Endpoint ?? DefaultEndpoint, UriKind.Absolute);
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
