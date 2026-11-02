namespace Sebastian.Libs.Catalog;

public sealed record CatalogModel(
    string Id,
    string Name,
    string? Family,
    bool Attachment,
    bool Reasoning,
    bool ToolCall,
    bool Temperature,
    string? Knowledge,
    string? ReleaseDate,
    string? LastUpdated,
    IReadOnlyList<string> InputModalities,
    IReadOnlyList<string> OutputModalities,
    bool OpenWeights,
    int? ContextLimit,
    int? OutputLimit,
    decimal? InputCost,
    decimal? OutputCost,
    decimal? CacheReadCost,
    string? CanonicalModelId);

public sealed record CatalogProvider(
    string Id,
    string Name,
    string? Api,
    IReadOnlyList<string> Env,
    string? Npm,
    string? Doc,
    IReadOnlyList<CatalogModel> Models);

public sealed record ModelCatalog(
    IReadOnlyList<CatalogProvider> Providers,
    DateTimeOffset CheckedAt,
    string? Warning)
{
    public CatalogProvider? FindProvider(string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : Providers.FirstOrDefault(
                provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
