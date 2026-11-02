using Microsoft.UI.Reactor.Core;
using Sebastian.Windows.Setup;

namespace Sebastian.Windows.Bootstrap;

internal static class AppContexts
{
    // Always provided by the root in Program.cs.
    internal static readonly Context<SetupService> Setup = new(default!);
}
