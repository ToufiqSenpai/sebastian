using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml.Controls;
using Sebastian.Windows.Pages;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Sebastian");

class App : Component
{
    static string ToTag(Route route) => route switch
    {
        Route.Home => "home",
        Route.Kanban => "kanban",
        Route.Project project => $"project:{project.Name}",
        Route.Settings => NavigationViewElement.SettingsTag,
        _ => "home",
    };

    static Route ToRoute(string tag) => tag switch
    {
        "home" => new Route.Home(),
        "kanban" => new Route.Kanban(),
        var value when value.StartsWith("project:", StringComparison.Ordinal) =>
            new Route.Project(value["project:".Length..]),
        NavigationViewElement.SettingsTag => new Route.Settings(),
        _ => new Route.Home(),
    };

    public override Element Render()
    {
        var nav = UseNavigation<Route>(new Route.Home());
        var wide = UseBreakpoint(900);

        var content = NavigationHost(nav, route => route switch
        {
            Route.Home => Component<HomePage>(),
            Route.Kanban => Component<PlaceholderPage, PlaceholderPageProps>(new("Kanban")),
            Route.Project project => Component<PlaceholderPage, PlaceholderPageProps>(new(project.Name)),
            Route.Settings => Component<PlaceholderPage, PlaceholderPageProps>(new("Settings")),
            _ => Component<HomePage>(),
        });

        var titleBar = TitleBar("Sebastian")
            .NoIcon()
            .BackButtonVisible(false)
            .PaneToggleButtonVisible(false)
            .Flex(shrink: 0);

        var navView = (NavigationView(
                [
                    NavItem("Home", icon: "Home", tag: "home"),
                    NavItem("Kanban", icon: "ViewAll", tag: "kanban"),
                    NavItemHeader("Projects"),
                    NavItem("sebastian", icon: "Folder", tag: "project:sebastian"),
                    NavItem("acme-web", icon: "Folder", tag: "project:acme-web"),
                ],
                content) with
            {
                IsPaneOpen = true,
                IsSettingsVisible = true,
            })
            .PaneDisplayMode(wide
                ? NavigationViewPaneDisplayMode.Left
                : NavigationViewPaneDisplayMode.LeftCompact)
            .OpenPaneLength(300)
            .PaneToggleButtonVisible(false)
            .BackButtonVisible(false)
            .AlwaysShowHeader(false)
            .TitleBarAutoPadding(false)
            .WithNavigation(nav, ToTag, ToRoute, () => new Route.Settings())
            .Resources(resources => resources
                .Set("NavigationViewDefaultPaneBackground", Theme.CardBackground)
                .Set("NavigationViewExpandedPaneBackground", Theme.CardBackground)
                .Set("NavigationViewContentBackground", Theme.SolidBackground))
            .Flex(grow: 1, basis: 0);

        return FlexColumn(titleBar, navView)
            .Background(Theme.SolidBackground);
    }
}
