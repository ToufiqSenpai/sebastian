using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sebastian.Libs.Catalog;

namespace Sebastian.Libs.Providers;

public sealed record ProviderCredentials(
    string? ApiKey,
    IReadOnlyDictionary<string, string> Values)
{
    public string? GetValue(string name)
    {
        return Values is not null &&
            Values.TryGetValue(name, out var value) &&
            !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }
}

public sealed record ProviderTestResult(
    bool Success,
    int ModelCount,
    bool UsedFallback,
    string? Error)
{
    public static ProviderTestResult Passed(int modelCount, bool usedFallback = false)
    {
        return new ProviderTestResult(true, modelCount, usedFallback, Error: null);
    }

    public static ProviderTestResult Failed(string error)
    {
        return new ProviderTestResult(false, 0, false, error);
    }
}

public sealed class ProviderTester : IDisposable
{
    private const string UserAgent = "Sebastian/0.1";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxErrorBody = 4096;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;

    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The HttpClient owns and disposes the handler.")]
    public ProviderTester(
        HttpMessageHandler? finalHandler = null,
        TimeSpan? timeout = null)
    {
        _httpClient = new HttpClient(finalHandler ?? new HttpClientHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<ProviderTestResult> TestAsync(
        ProviderAdapter adapter,
        string baseUrl,
        ProviderCredentials credentials,
        IReadOnlyList<CatalogModel> catalogModels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(catalogModels);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var apiKey = credentials.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderTestResult.Failed("Enter an API key.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            using var response = await SendModelsRequestAsync(
                    adapter,
                    baseUrl,
                    apiKey,
                    timeoutCts.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return adapter == ProviderAdapter.Azure
                    ? await FailureAsync(
                            response,
                            "The model list endpoint was not found. Check that the base URL ends with /openai/v1",
                            timeoutCts.Token)
                        .ConfigureAwait(false)
                    : await FallbackAsync(
                            adapter,
                            baseUrl,
                            apiKey,
                            catalogModels,
                            timeoutCts.Token)
                        .ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode)
            {
                return await FailureAsync(
                        response,
                        "The provider rejected the request",
                        timeoutCts.Token)
                    .ConfigureAwait(false);
            }

            var count = await ReadModelCountAsync(response.Content, timeoutCts.Token)
                .ConfigureAwait(false);
            return count is null
                ? ProviderTestResult.Failed("The provider returned a response that could not be read.")
                : ProviderTestResult.Passed(count.Value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var seconds = _timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            return ProviderTestResult.Failed(
                $"The provider did not respond within {seconds} seconds.");
        }
        catch (HttpRequestException exception)
        {
            return ProviderTestResult.Failed($"Could not reach the provider: {exception.Message}");
        }
        catch (IOException exception)
        {
            return ProviderTestResult.Failed($"Could not read the provider response: {exception.Message}");
        }
        catch (JsonException)
        {
            return ProviderTestResult.Failed("The provider returned a response that could not be read.");
        }
    }

    private async Task<HttpResponseMessage> SendModelsRequestAsync(
        ProviderAdapter adapter,
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUri(adapter, baseUrl));
        AddAuthentication(request, adapter, apiKey);
        request.Headers.Accept.ParseAdd("application/json");
        return await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProviderTestResult> FallbackAsync(
        ProviderAdapter adapter,
        string baseUrl,
        string apiKey,
        IReadOnlyList<CatalogModel> catalogModels,
        CancellationToken cancellationToken)
    {
        var model = catalogModels.FirstOrDefault(candidate => candidate.ToolCall);
        if (model is null)
        {
            return ProviderTestResult.Failed(
                "The model list endpoint returned 404 and this provider has no model with tool calling for a fallback test.");
        }

        var payload = new ChatFallbackRequest(
            model.Id,
            MaxTokens: 1,
            [
                new ChatFallbackMessage(
                    "user",
                    "Reply with the single word OK."),
            ]);
        var json = JsonSerializer.Serialize(
            payload,
            ProviderJsonContext.Default.ChatFallbackRequest);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ChatUri(adapter, baseUrl))
        {
            Content = content,
        };
        AddAuthentication(request, adapter, apiKey);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? ProviderTestResult.Passed(modelCount: 0, usedFallback: true)
            : await FailureAsync(
                    response,
                    "The model list endpoint returned 404 and the fallback chat completion failed",
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private static Uri ModelsUri(ProviderAdapter adapter, string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        var path = adapter == ProviderAdapter.Anthropic &&
            !trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? "/v1/models"
            : "/models";

        return new Uri(trimmed + path, UriKind.Absolute);
    }

    private static Uri ChatUri(ProviderAdapter adapter, string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        var path = adapter switch
        {
            ProviderAdapter.OpenAiCompatible => "/chat/completions",
            ProviderAdapter.Anthropic when trimmed.EndsWith(
                "/v1",
                StringComparison.OrdinalIgnoreCase) => "/messages",
            ProviderAdapter.Anthropic => "/v1/messages",
            ProviderAdapter.Azure => "/chat/completions",
            _ => throw new ArgumentOutOfRangeException(nameof(adapter)),
        };

        return new Uri(trimmed + path, UriKind.Absolute);
    }

    private static void AddAuthentication(
        HttpRequestMessage request,
        ProviderAdapter adapter,
        string apiKey)
    {
        if (adapter == ProviderAdapter.Anthropic)
        {
            _ = request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            _ = request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        }
        else if (adapter == ProviderAdapter.Azure)
        {
            _ = request.Headers.TryAddWithoutValidation("api-key", apiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private static async Task<int?> ReadModelCountAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Array
                ? root.GetArrayLength()
                : ReadDataCount(root);
        }
    }

    private static int? ReadDataCount(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array
            ? data.GetArrayLength()
            : null;
    }

    private static async Task<ProviderTestResult> FailureAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken cancellationToken)
    {
        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        body = body.Length <= MaxErrorBody ? body : body[..MaxErrorBody] + "…";

        var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
        return ProviderTestResult.Failed(
            string.IsNullOrWhiteSpace(detail)
                ? $"{message} (HTTP {status})."
                : $"{message} (HTTP {status}): {detail}");
    }
}

internal sealed record ChatFallbackRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatFallbackMessage> Messages);

internal sealed record ChatFallbackMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

[JsonSerializable(typeof(ChatFallbackRequest))]
internal sealed partial class ProviderJsonContext : JsonSerializerContext;
