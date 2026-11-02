using Microsoft.Extensions.DependencyInjection;
using Sebastian.Libs;
using Sebastian.Libs.GitHub.App;
using Sebastian.Libs.GitHub.Personal;
using Sebastian.Libs.Settings;
using Sebastian.Windows.Settings;
using Sebastian.Windows.Setup;

namespace Sebastian.Windows.Bootstrap;

internal static class AppServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        _ = services.AddSingleton<ISettings>(_ => new LocalSettings());
        _ = services.AddSingleton(sp => new SetupService(
            sp.GetRequiredService<KeyValueStore>(),
            sp.GetRequiredService<GitHubPersonalService>(),
            sp.GetRequiredService<GitHubAppSetupService>()));
        _ = services.AddSebastianLibs();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
