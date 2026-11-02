using Microsoft.Extensions.DependencyInjection;
using Sebastian.Libs;
using Sebastian.Libs.Settings;
using Sebastian.Windows.Settings;
using Sebastian.Windows.Setup;

namespace Sebastian.Windows.Bootstrap;

internal static class AppServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        _ = services.AddSingleton<ISettings, LocalSettings>();
        _ = services.AddSingleton<SetupService>();
        _ = services.AddSebastianLibs();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
