using Sebastian.Libs.Catalog;

namespace Sebastian.Libs.Tests.Providers;

internal static class TestCatalog
{
    public static CatalogModel Model(string id, bool toolCall = true)
    {
        return new CatalogModel(
            id,
            id,
            Family: null,
            Attachment: false,
            Reasoning: false,
            ToolCall: toolCall,
            Temperature: true,
            Knowledge: null,
            ReleaseDate: null,
            LastUpdated: null,
            InputModalities: ["text"],
            OutputModalities: ["text"],
            OpenWeights: false,
            ContextLimit: 128000,
            OutputLimit: 8192,
            InputCost: 0.1m,
            OutputCost: 0.2m,
            CacheReadCost: null,
            CanonicalModelId: null);
    }

    public static CatalogProvider Provider(
        string id,
        string? npm = null,
        string? api = null,
        string[]? env = null,
        string? doc = null,
        params CatalogModel[] models)
    {
        return new CatalogProvider(
            id,
            id,
            api,
            env ?? [$"{id.ToUpperInvariant().Replace('-', '_')}_API_KEY"],
            npm,
            doc,
            models.Length == 0 ? [Model("gpt-test")] : models);
    }
}
