using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Pages;

internal sealed record PlaceholderPageProps(string Heading);

internal sealed class PlaceholderPage : Component<PlaceholderPageProps>
{
    public override Element Render()
    {
        return Border(
            VStack(8,
                Title(Props.Heading),
                Body("Coming soon.").Foreground(Theme.SecondaryText)
            ).HAlign(HorizontalAlignment.Center)
             .VAlign(VerticalAlignment.Center)
        ).Background(Theme.SolidBackground)
         .Flex(grow: 1);
    }
}
