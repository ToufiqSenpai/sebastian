using Microsoft.Extensions.DependencyInjection;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.GitHub.Personal;
using Sebastian.Libs.Providers;
using Sebastian.Libs.Settings;

namespace Sebastian.Libs;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSebastianLibs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<KeyValueStore>();
        _ = services.AddSingleton<GitHubPersonalService>();
        _ = services.AddSingleton<IGitHubAppService, GitHubAppService>();
        _ = services.AddSingleton<GitHubAppSetupService>();
        _ = services.AddSingleton<CatalogService>();
        _ = services.AddSingleton<ProviderTester>();
        _ = services.AddSingleton<ProviderSetupService>();

        return services;
    }
}
