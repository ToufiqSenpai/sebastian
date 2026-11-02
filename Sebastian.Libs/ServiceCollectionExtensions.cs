using Microsoft.Extensions.DependencyInjection;
using Sebastian.Libs.GitHub.Personal;
using Sebastian.Libs.Settings;

namespace Sebastian.Libs;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSebastianLibs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<KeyValueStore>();
        _ = services.AddSingleton<IGitHubPersonalService, GitHubPersonalService>();

        return services;
    }
}
