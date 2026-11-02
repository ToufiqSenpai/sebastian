using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using AppContexts = Sebastian.Windows.Bootstrap.AppContexts;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.Providers;
using Sebastian.Windows.Bootstrap;
using Sebastian.Windows.Setup;
using static Microsoft.UI.Reactor.Factories;

using var services = AppServices.Build();
var setup = services.GetRequiredService<SetupService>();
var catalog = services.GetRequiredService<CatalogService>();
var providers = services.GetRequiredService<ProviderSetupService>();

ReactorApp.Run("Sebastian", _ =>
    Component<App>()
        .Provide(AppContexts.Setup, setup)
        .Provide(AppContexts.Catalog, catalog)
        .Provide(AppContexts.Providers, providers));
