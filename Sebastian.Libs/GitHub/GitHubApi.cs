using Microsoft.Kiota.Http.HttpClientLibrary;
using Sebastian.Libs.Http;

namespace Sebastian.Libs.GitHub;

internal static class GitHubApi
{
    public const string BaseUrl = "https://api.github.com";
    public const string ApiVersion = "2022-11-28";

    public static HttpClient CreateClient(HttpMessageHandler? finalHandler, TimeSpan timeout)
    {
        var client = KiotaClientFactory.Create(finalHandler);
        client.BaseAddress = new Uri(BaseUrl, UriKind.Absolute);
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppHttp.UserAgent);
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);

        return client;
    }
}
