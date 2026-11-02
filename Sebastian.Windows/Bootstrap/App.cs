using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml.Controls;
using Sebastian.Windows.Pages;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Bootstrap;

internal sealed class App : Component
{
    private static string ToTag(Route route)
    {
        return route switch
        {
            Route.Home => "home",
            Route.Kanban => "kanban",
            Route.Project project => $"project:{project.Name}",
            Route.Settings => NavigationViewElement.SettingsTag,
            Route.Setup => "setup",
            _ => "home",
        };
    }

    private static Route ToRoute(string tag)
    {
        return tag switch
        {
            "home" => new Route.Home(),
            "kanban" => new Route.Kanban(),
            var value when value.StartsWith("project:", StringComparison.Ordinal) =>
                new Route.Project(value["project:".Length..]),
            NavigationViewElement.SettingsTag => new Route.Settings(),
            "setup" => new Route.Setup(),
            _ => new Route.Home(),
        };
    }

    public override Element Render()
    {
        var nav = UseNavigation<Route>(new Route.Setup());
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

        if (nav.CurrentRoute is Route.Setup)
        {
            var setup = FlexColumn(
                Component<SetupPage>()) with
            {
                JustifyContent = FlexJustify.Center,
                AlignItems = FlexAlign.Stretch,
            };

            return FlexColumn(
                    titleBar,
                    setup.Flex(grow: 1, basis: 0))
                .Background(Theme.SolidBackground);
        }

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
