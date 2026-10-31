using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Pages;

class HomePage : Component
{
    public override Element Render()
    {
        var footer = Border(
                HStack(6,
                    TextBlock("\uE8B7")
                        .FontFamily("Segoe Fluent Icons")
                        .FontSize(14)
                        .Foreground(Theme.SecondaryText),
                    Body("sebastian").Foreground(Theme.SecondaryText),
                    TextBlock("\uE70D")
                        .FontFamily("Segoe Fluent Icons")
                        .FontSize(10)
                        .Foreground(Theme.SecondaryText)
                ).Padding(16, 10)
            ).Background(Theme.SubtleFill)
             .BorderBrush(Theme.DividerStroke)
             .BorderThickness(0, 1, 0, 0)
             .CornerRadius(0, 0, 12, 12);

        var card = Border(
                VStack(0,
                    Border(
                        Component<ComposerInput>()
                    ).Padding(24, 16, 24, 0),

                    Grid(
                        [GridSize.Auto, GridSize.Star(), GridSize.Auto, GridSize.Auto],
                        [GridSize.Auto],
                        [
                            HStack(8,
                                TextBlock("\uE710")
                                    .FontFamily("Segoe Fluent Icons")
                                    .FontSize(16),
                                Body("Interactive")
                            ).Grid(row: 0, column: 0)
                             .VAlign(VerticalAlignment.Center),

                            Empty().Grid(row: 0, column: 1),

                            Body("Opus 5.5 · Max")
                                .Foreground(Theme.SecondaryText)
                                .Grid(row: 0, column: 2)
                                .Margin(0, 0, 12, 0)
                                .VAlign(VerticalAlignment.Center),

                            Button(TextBlock("\uE74A")
                                    .FontFamily("Segoe Fluent Icons")
                                    .FontSize(16)
                                    .Foreground(Theme.Ref("TextOnAccentFillColorPrimaryBrush")))
                                .AccentButton()
                                .Width(40)
                                .Height(40)
                                .MinWidth(40)
                                .MinHeight(40)
                                .Padding(0)
                                .CornerRadius(20)
                                .HorizontalContentAlignment(HorizontalAlignment.Center)
                                .VerticalContentAlignment(VerticalAlignment.Center)
                                .AutomationName("Send")
                                .Grid(row: 0, column: 3),
                        ]
                    ).Padding(24, 8, 16, 16),

                    footer
                )
            ).Background(Theme.CardBackground)
             .WithBorder(Theme.CardStroke, 1)
             .CornerRadius(12)
             .Translation(0, 0, 32)
             .Set(border => border.Shadow = new ThemeShadow())
             .Margin(12);

        var page = VStack(8,
                Title("What are we building today?")
                    .HAlign(HorizontalAlignment.Center),
                card
            ).MaxWidth(864)
             .VAlign(VerticalAlignment.Center);

        return Border(page)
            .Background(Theme.SolidBackground)
            .Padding(48, 24)
            .Flex(grow: 1);
    }
}

class ComposerInput : Component
{
    public override Element Render()
    {
        var (draft, setDraft) = UseState("");

        return TextBox(draft, setDraft, placeholderText: "Describe a spec, bug, or refactor...")
            .FontSize(14)
            .Background(Theme.Ref("ControlFillColorTransparentBrush"))
            .BorderThickness(0)
            .Padding(0)
            .AcceptsReturn(true)
            .TextWrapping(TextWrapping.Wrap)
            .MinHeight(44)
            .AutomationName("Describe a spec, bug, or refactor");
    }
}
