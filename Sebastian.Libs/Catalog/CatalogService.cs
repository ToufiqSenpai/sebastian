using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sebastian.Libs.Http;

namespace Sebastian.Libs.Catalog;

public sealed class CatalogService : IDisposable
{
    private const string SourceUrl = "https://models.dev/api.json";
    private const long MaxBytes = 64L * 1024 * 1024;
    private const int MaxErrorBody = 4096;

    private static readonly string TooLargeMessage =
        $"models.dev returned a document larger than {MaxBytes / (1024 * 1024)} MB.";

    private static readonly TimeSpan NoEtagRefreshInterval = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly string _modelsPath;
    private readonly string _metaPath;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ModelCatalog? _catalog;

    public CatalogService(
        HttpMessageHandler? finalHandler = null,
        string? cacheDirectory = null,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = finalHandler is null
            ? new HttpClient()
            : new HttpClient(finalHandler, disposeHandler: false);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(AppHttp.UserAgent);

        _cacheDirectory = Path.GetFullPath(
            cacheDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sebastian",
                "catalog"));
        _modelsPath = Path.Combine(_cacheDirectory, "models.json");
        _metaPath = Path.Combine(_cacheDirectory, "models.meta.json");
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _gate.Dispose();
    }

    public async Task<ModelCatalog> GetAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _catalog);
        if (existing is not null)
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = Volatile.Read(ref _catalog);
            if (existing is not null)
            {
                return existing;
            }

            var catalog = await LoadAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _catalog, catalog);
            return catalog;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<ModelCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        var meta = TryReadMeta();
        var cached = TryReadCachedProviders();
        var now = _timeProvider.GetUtcNow();

        if (cached is not null &&
            meta is not null &&
            meta.Etag is null &&
            now - meta.FetchedAt < NoEtagRefreshInterval)
        {
            return new ModelCatalog(cached, meta.CheckedAt, Warning: null);
        }

        var (refreshed, error) = await RefreshAsync(cached, meta, cancellationToken).ConfigureAwait(false);
        return refreshed ?? CachedOrThrow(cached, meta, error);
    }

    private static ModelCatalog CachedOrThrow(
        List<CatalogProvider>? cached,
        CatalogMeta? meta,
        string? error)
    {
        return cached is not null
            ? new ModelCatalog(
                cached,
                meta?.CheckedAt ?? DateTimeOffset.MinValue,
                $"Using cached catalog: {error}")
            : throw new CatalogException($"The provider catalog could not be loaded: {error}");
    }

    private async Task<(ModelCatalog? Catalog, string? Error)> RefreshAsync(
        List<CatalogProvider>? cached,
        CatalogMeta? meta,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
        if (cached is not null && meta?.Etag is not null)
        {
            _ = request.Headers.TryAddWithoutValidation("If-None-Match", meta.Etag);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (cached is null)
                {
                    return (null, "models.dev returned 304 Not Modified but there is no cached catalog.");
                }

                var now = _timeProvider.GetUtcNow();
                var updated = new CatalogMeta(
                    meta?.Etag,
                    meta?.FetchedAt ?? now,
                    now,
                    meta?.Bytes ?? 0);
                TryWriteMeta(updated);
                return (new ModelCatalog(cached, now, Warning: null), null);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadErrorBodyAsync(response.Content, cancellationToken)
                    .ConfigureAwait(false);
                var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                return (null, string.IsNullOrWhiteSpace(body)
                    ? $"models.dev returned HTTP {status}."
                    : $"models.dev returned HTTP {status}: {body}");
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return (null, TooLargeMessage);
            }

            var bytes = await ReadCappedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return (null, TooLargeMessage);
            }

            var providers = ParseProviders(bytes);
            var fetchedAt = _timeProvider.GetUtcNow();
            WriteCache(bytes, response.Headers.ETag?.ToString(), fetchedAt);
            return (new ModelCatalog(providers, fetchedAt, Warning: null), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var seconds = _timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            return (null, $"models.dev did not respond within {seconds} seconds.");
        }
        catch (HttpRequestException exception)
        {
            return (null, $"Could not reach models.dev: {exception.Message}");
        }
        catch (IOException exception)
        {
            return (null, $"Could not read models.dev: {exception.Message}");
        }
        catch (JsonException)
        {
            return (null, "models.dev returned a response that could not be read.");
        }
    }

    private static async Task<byte[]?> ReadCappedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            await content.LoadIntoBufferAsync(MaxBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        return await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadErrorBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Length <= MaxErrorBody ? body : body[..MaxErrorBody] + "…";
    }

    private void WriteCache(byte[] bytes, string? etag, DateTimeOffset now)
    {
        _ = Directory.CreateDirectory(_cacheDirectory);
        var tempPath = _modelsPath + ".tmp";

        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, _modelsPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }

        TryWriteMeta(new CatalogMeta(etag, now, now, bytes.LongLength));
    }

    private void TryWriteMeta(CatalogMeta meta)
    {
        var tempPath = _metaPath + ".tmp";

        try
        {
            _ = Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(meta, CatalogJsonContext.Default.CatalogMeta));
            File.Move(tempPath, _metaPath, overwrite: true);
        }
        catch (IOException)
        {
            // Metadata is an optimization; the catalog cache remains usable without it.
        }
        catch (UnauthorizedAccessException)
        {
            // Metadata is an optimization; the catalog cache remains usable without it.
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private CatalogMeta? TryReadMeta()
    {
        try
        {
            return !File.Exists(_metaPath)
                ? null
                : JsonSerializer.Deserialize(
                    File.ReadAllText(_metaPath),
                    CatalogJsonContext.Default.CatalogMeta);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private List<CatalogProvider>? TryReadCachedProviders()
    {
        try
        {
            if (!File.Exists(_modelsPath))
            {
                return null;
            }

            using var stream = new FileStream(
                _modelsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var document = JsonDocument.Parse(stream);
            return ParseProviders(document.RootElement);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<CatalogProvider> ParseProviders(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        return ParseProviders(document.RootElement);
    }

    private static List<CatalogProvider> ParseProviders(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The catalog root must be an object.");
        }

        var providers = new List<CatalogProvider>();

        foreach (var property in root.EnumerateObject())
        {
            var element = property.Value;
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadString(element, "id")?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                id = property.Name.Trim();
            }

            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var name = ReadString(element, "name")?.Trim();
            providers.Add(new CatalogProvider(
                id,
                string.IsNullOrEmpty(name) ? id : name,
                ReadString(element, "api"),
                ReadStringArray(element, "env"),
                ReadString(element, "npm"),
                ReadString(element, "doc"),
                ReadModels(element)));
        }

        return providers.Count == 0
            ? throw new JsonException("The catalog contains no providers.")
            : providers;
    }

    private static List<CatalogModel> ReadModels(JsonElement provider)
    {
        if (!provider.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new List<CatalogModel>();

        foreach (var property in models.EnumerateObject())
        {
            var element = property.Value;
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadString(element, "id")?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                id = property.Name.Trim();
            }

            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var name = ReadString(element, "name")?.Trim();
            var limit = GetObject(element, "limit");
            var cost = GetObject(element, "cost");
            var modalities = GetObject(element, "modalities");

            result.Add(new CatalogModel(
                id,
                string.IsNullOrEmpty(name) ? id : name,
                ReadString(element, "family"),
                ReadBoolean(element, "attachment"),
                ReadBoolean(element, "reasoning"),
                ReadBoolean(element, "tool_call"),
                ReadBoolean(element, "temperature"),
                ReadString(element, "knowledge"),
                ReadString(element, "release_date"),
                ReadString(element, "last_updated"),
                ReadStringArray(modalities, "input"),
                ReadStringArray(modalities, "output"),
                ReadBoolean(element, "open_weights"),
                ReadNestedInt32(limit, "context"),
                ReadNestedInt32(limit, "output"),
                ReadNestedDecimal(cost, "input"),
                ReadNestedDecimal(cost, "output"),
                ReadNestedDecimal(cost, "cache_read"),
                ReadString(element, "canonical_model_id")));
        }

        return result;
    }

    private static JsonElement? GetObject(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static string? ReadString(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static List<string> ReadStringArray(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Array
            ? ReadStringArray(value)
            : [];
    }

    private static List<string> ReadStringArray(JsonElement? parent, string property)
    {
        return parent is { } value
            ? ReadStringArray(value, property)
            : [];
    }

    private static List<string> ReadStringArray(JsonElement array)
    {
        var result = new List<string>();

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value.Trim());
                }
            }
        }

        return result;
    }

    private static bool ReadBoolean(JsonElement parent, string property)
    {
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.True;
    }

    private static int? ReadNestedInt32(JsonElement? parent, string property)
    {
        return parent is { } value &&
            value.TryGetProperty(property, out var child) &&
            child.ValueKind == JsonValueKind.Number &&
            child.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static decimal? ReadNestedDecimal(JsonElement? parent, string property)
    {
        return parent is { } value &&
            value.TryGetProperty(property, out var child) &&
            child.ValueKind == JsonValueKind.Number &&
            child.TryGetDecimal(out var result)
            ? result
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temporary file.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temporary file.
        }
    }
}

[JsonSerializable(typeof(CatalogMeta))]
internal sealed partial class CatalogJsonContext : JsonSerializerContext;

internal sealed record CatalogMeta(
    string? Etag,
    DateTimeOffset FetchedAt,
    DateTimeOffset CheckedAt,
    long Bytes);
