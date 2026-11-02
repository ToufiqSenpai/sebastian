using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Sebastian.Windows.Helpers;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Pages;

internal sealed class HomePage : Component
{
    private static readonly string[] Projects = ["sebastian", "acme-web"];
    private static readonly string[] Modes = ["Standard Mode", "Spec Mode"];
    private static readonly string[] Models = ["Opus 5.5", "DeepSeek V4.1 Flash"];
    private static readonly string[] Efforts = ["Low", "Medium", "High", "Max"];

    public override Element Render()
    {
        // Keep the composer column proportional: a fixed 864 cap fills the whole
        // pane once the default window is below ~1260 DIP, which reads as oversized.
        (var windowWidth, var _) = UseWindowSize();
        var columnWidth = Math.Min(864, Math.Max(480, windowWidth * 0.5));
        var roomy = windowWidth >= 720;

        (var draft, var setDraft) = UseState("");
        (var project, var setProject) = UsePersisted("project:active", Projects[0], PersistedScope.Application);
        (var mode, var setMode) = UsePersisted("chat:mode", Modes[0], PersistedScope.Application);
        (var model, var setModel) = UsePersisted("agent:model", Models[0], PersistedScope.Application);
        (var effort, var setEffort) = UsePersisted("agent:effort", Efforts[^1], PersistedScope.Application);
        (var attachments, var updateAttachments) = UseReducer(Array.Empty<Attachment>());
        var window = UseWindow();

        async void PickFiles()
        {
            if (window is null)
            {
                return;
            }

            var files = await FilePicker.PickFilesAsync(window, "*").ConfigureAwait(true);
            if (files is null || files.Count == 0)
            {
                return;
            }

            updateAttachments(list =>
            [
                .. list,
                .. files.Where(file => !list.Any(a => a.Path == file.Path))
                        .Select(file => new Attachment(file.Name, file.Path)),
            ]);
        }

        Element MenuChip(Element? icon, string label, string automationName, MenuFlyoutItemBase[] items)
        {
            var parts = new List<Element>();
            if (icon is not null)
            {
                parts.Add(icon.VAlign(VerticalAlignment.Center));
            }

            parts.Add(Body(label)
                .Foreground(Theme.SecondaryText)
                .VAlign(VerticalAlignment.Center));
            parts.Add(TextBlock("\uE70D")
                .FontFamily(IconFont.Family)
                .FontSize(12)
                .Foreground(Theme.SecondaryText)
                .VAlign(VerticalAlignment.Center));

            return MenuFlyout(
                Button(HStack(6, [.. parts]))
                    .SubtleButton()
                    .Padding(horizontal: 4, vertical: 2)
                    .AutomationName(automationName),
                items);
        }

        var projectChip = MenuChip(
            TextBlock("\uE8B7").FontFamily(IconFont.Family).FontSize(14).Foreground(Theme.SecondaryText),
            project,
            "Project",
            [.. Projects.Select(p => RadioMenuItem(p, "project", p == project, () => setProject(p)))]);

        var modeChip = MenuChip(
            TextBlock("\uE77B").FontFamily(IconFont.Family).FontSize(14).Foreground(Theme.SecondaryText),
            mode,
            "Mode",
            [.. Modes.Select(m => RadioMenuItem(m, "mode", m == mode, () => setMode(m)))]);

        var modelChip = MenuChip(
            null,
            $"{model} \u00B7 {effort}",
            "Model and reasoning effort",
            [
                MenuSubItem("Model",
                    [.. Models.Select(m => RadioMenuItem(m, "model", m == model, () => setModel(m)))]),
                MenuSubItem("Effort",
                    [.. Efforts.Select(e => RadioMenuItem(e, "effort", e == effort, () => setEffort(e)))]),
            ]);

        var plusButton = MenuFlyout(
            Button(TextBlock("\uE710").FontFamily(IconFont.Family).FontSize(16))
                .SubtleButton()
                .Width(32)
                .Height(32)
                .MinWidth(32)
                .MinHeight(32)
                .Padding(0)
                .CornerRadius(16)
                .Margin(-8, 0, 0, 0)
                .HorizontalContentAlignment(HorizontalAlignment.Center)
                .VerticalContentAlignment(VerticalAlignment.Center)
                .AutomationName("Add file"),
            [MenuItem("Add file\u2026", PickFiles)]);

        var sendButton = Button(
                TextBlock("\uE74A")
                    .FontFamily(IconFont.Family)
                    .FontSize(14)
                    .Foreground(Theme.Ref("TextOnAccentFillColorPrimaryBrush")))
            .AccentButton()
            .Width(32)
            .Height(32)
            .MinWidth(32)
            .MinHeight(32)
            .Padding(0)
            .CornerRadius(16)
            .HorizontalContentAlignment(HorizontalAlignment.Center)
            .VerticalContentAlignment(VerticalAlignment.Center)
            .AutomationName("Send")
            .IsEnabled(!string.IsNullOrWhiteSpace(draft));

        var input = Border(
                TextBox(draft, setDraft, placeholderText: "Describe a spec, bug, or refactor...")
                    .FontSize(14)
                    .Background(Theme.Ref("ControlFillColorTransparentBrush"))
                    .BorderThickness(0)
                    .Padding(0)
                    .AcceptsReturn(true)
                    .TextWrapping(TextWrapping.Wrap)
                    .MinHeight(44)
                    .MaxHeight(160)
                    .AutomationName("Describe a spec, bug, or refactor")
            ).Padding(24, 16, 24, 0);

        var attachmentRow = When(attachments.Length > 0, () =>
            FlexRow([.. attachments.Select(a =>
                    Border(HStack(6,
                            TextBlock("\uE8A5")
                                .FontFamily(IconFont.Family)
                                .FontSize(12)
                                .Foreground(Theme.SecondaryText),
                            Body(a.Name).Foreground(Theme.SecondaryText),
                            Button(TextBlock("\uE711")
                                        .FontFamily(IconFont.Family)
                                        .FontSize(10),
                                    () => updateAttachments(list => [.. list.Where(x => x.Path != a.Path)]))
                                .SubtleButton()
                                .Width(20)
                                .Height(20)
                                .MinWidth(20)
                                .MinHeight(20)
                                .Padding(0)
                                .CornerRadius(10)
                                .AutomationName($"Remove {a.Name}")
                        ))
                        .Padding(horizontal: 10, vertical: 4)
                        .Background(Theme.SubtleFill)
                        .CornerRadius(12)
                        .WithKey(a.Path)
                )])
                with
            { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 }
        ).Padding(24, 8, 24, 0);

        var actionRow = Grid(
            [GridSize.Auto, GridSize.Star(), GridSize.Auto, GridSize.Auto],
            [GridSize.Auto],
            [
                plusButton.Grid(row: 0, column: 0),
                Empty().Grid(row: 0, column: 1),
                (roomy ? modelChip : Empty()).Grid(row: 0, column: 2)
                    .Margin(0, 0, 8, 0)
                    .VAlign(VerticalAlignment.Center),
                sendButton.Grid(row: 0, column: 3),
            ]
        ).Padding(24, 8, 16, 16);

        var card = Border(VStack(0, input, attachmentRow, actionRow))
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1)
            .CornerRadius(16)
            .Translation(0, 0, 16)
            .Set(border => border.Shadow = new ThemeShadow())
            .Margin(12, 0, 12, 12);

        var contextChips = (FlexRow([projectChip, modeChip]) with
        {
            Wrap = FlexWrap.Wrap,
            ColumnGap = 12,
            RowGap = 4,
        })
            .HAlign(HorizontalAlignment.Left)
            .Margin(36, 0, 36, 0);

        var page = VStack(8,
                Title("What are we building today?")
                    .Margin(0, 0, 0, 28)
                    .HAlign(HorizontalAlignment.Center),
                contextChips,
                card
            ).MaxWidth(columnWidth)
             .VAlign(VerticalAlignment.Center);

        return Border(page)
            .Background(Theme.SolidBackground)
            .Padding(48, 24)
            .Flex(grow: 1);
    }

    private sealed record Attachment(string Name, string Path);
}
