using Microsoft.UI.Reactor.Core;
using Sebastian.Libs.Catalog;
using Sebastian.Libs.Providers;
using Sebastian.Windows.Setup;

namespace Sebastian.Windows.Bootstrap;

internal static class AppContexts
{
    // Always provided by the root in Program.cs.
    internal static readonly Context<SetupService> Setup = new(default!);
    internal static readonly Context<CatalogService> Catalog = new(default!);
    internal static readonly Context<ProviderSetupService> Providers = new(default!);
}
