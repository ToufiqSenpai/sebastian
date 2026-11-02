using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal static class SetupLayout
{
    public static Element Actions(bool wide, Element back, Element next)
    {
        return wide
            ? Grid(
                [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                [GridSize.Auto],
                [
                    back.Grid(row: 0, column: 0),
                    Empty().Grid(row: 0, column: 1),
                    next.Grid(row: 0, column: 2),
                ])
            : VStack(
                8,
                next.HAlign(HorizontalAlignment.Right),
                back.HAlign(HorizontalAlignment.Left));
    }

    public static double PageHeight(double windowHeight)
    {
        return Math.Max(320, windowHeight - 160);
    }
}
