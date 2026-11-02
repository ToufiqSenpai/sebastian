using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Sebastian.Libs.Catalog;
using Sebastian.Windows.Helpers;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

namespace Sebastian.Windows.Components.Setup;

internal sealed record ProviderPickerProps(
    ModelCatalog? Data,
    Exception? Error,
    string SelectedProviderId,
    string? SelectedName,
    bool Busy,
    double ContentWidth,
    bool Wide,
    Action<CatalogProvider> Select,
    Action Retry);

internal sealed class ProviderPicker : Component<ProviderPickerProps>
{
    public override Element Render()
    {
        (var search, var setSearch) = UseState("");
        (var pickerOpen, var setPickerOpen) = UseState(false);
        var pickerPresenterStyle = UseMemo(static () => CreatePickerPresenterStyle(), []);
        var (searchRef, focusSearch) = this.UseElementFocus();
        var (listRef, focusList) = this.UseElementFocus();
        var selectorRef = this.UseElementRef<Button>();

        UseEffect(
            () =>
            {
                if (pickerOpen)
                {
                    focusSearch();
                }
            },
            pickerOpen);

        var data = Props.Data;
        if (data is null)
        {
            return BuildFallback(Props.Error, Props.Retry);
        }

        var filtered = FilterProviders(data, search.Trim());
        var selectedIndex = Array.FindIndex(filtered, item => item.Id == Props.SelectedProviderId);

        void Pick(CatalogProvider chosen)
        {
            Props.Select(chosen);
            setPickerOpen(false);
            setSearch("");
            selectorRef.Current?.Flyout?.Hide();
        }

        var pickerBody = BuildPickerBody(
            filtered,
            selectedIndex,
            Props,
            Pick,
            setSearch,
            listRef);

        var pickerContent = Grid(
                [GridSize.Star()],
                [GridSize.Star(), GridSize.Auto],
                [
                    pickerBody.Grid(row: 0, column: 0),
                    BuildSearchBox(search, setSearch, Props, filtered, Pick, focusList, searchRef)
                        .Margin(0, 12, 0, 0)
                        .Grid(row: 1, column: 0),
                ])
            .Padding(12)
            .Width(Math.Max(280, Props.ContentWidth))
            .Height(Props.Wide ? 420 : 300);

        var providerPicker = (Flyout(
                BuildTrigger(Props.SelectedName, Props.Busy, selectorRef),
                pickerContent) with
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            OnOpened = () => setPickerOpen(true),
            OnClosed = () =>
            {
                setPickerOpen(false);
                setSearch("");
            },
        })
            .Set(
                flyout =>
                {
                    flyout.AllowFocusOnInteraction = true;
                    flyout.FlyoutPresenterStyle = pickerPresenterStyle;
                });

        return VStack(
            0,
            BuildWarning(data.Warning).Margin(0, 0, 0, 12),
            providerPicker);
    }

    private static StackElement BuildFallback(Exception? error, Action retry)
    {
        return error is null
            ? HStack(
                8,
                ProgressRing().Width(16).Height(16),
                Body("Loading provider catalog…")
                    .Foreground(Theme.SecondaryText))
            : VStack(
                16,
                InfoBar("Could not load the provider catalog", error.Message)
                    .Error()
                    .IsClosable(false),
                Button("Retry", retry)
                    .AccentButton()
                    .AutomationName("Retry provider catalog"));
    }

    private static Element BuildWarning(string? warning)
    {
        return warning is null
            ? Empty()
            : InfoBar("Using cached catalog", warning)
                .Warning()
                .IsClosable(false);
    }

    private static Element BuildPickerBody(
        CatalogProvider[] filtered,
        int selectedIndex,
        ProviderPickerProps props,
        Action<CatalogProvider> pick,
        Action<string> setSearch,
        ElementRef listRef)
    {
        return filtered.Length == 0
            ? VStack(
                8,
                Body("No providers match.")
                    .Foreground(Theme.SecondaryText),
                Button("Clear search", () => setSearch(""))
                    .SubtleButton()
                    .HAlign(HorizontalAlignment.Left))
            : BuildProviderList(
                filtered,
                selectedIndex,
                props.Busy,
                props.Select,
                pick,
                listRef);
    }

    private static ButtonElement BuildTrigger(string? name, bool busy, ElementRef<Button> selectorRef)
    {
        var triggerContent = Grid(
            [GridSize.Star(), GridSize.Auto],
            [GridSize.Auto],
            [
                BodyStrong(name ?? "Select a provider")
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .VAlign(VerticalAlignment.Center)
                    .Grid(row: 0, column: 0),
                TextBlock("\uE70D")
                    .FontFamily(IconFont.Family)
                    .FontSize(12)
                    .Foreground(Theme.SecondaryText)
                    .VAlign(VerticalAlignment.Center)
                    .Grid(row: 0, column: 1),
            ]) with
        {
            ColumnSpacing = 8,
        };

        return Button(triggerContent)
            .HAlign(HorizontalAlignment.Stretch)
            .Padding(12, 8)
            .Background(Theme.ControlFill)
            .BorderBrush(Theme.ControlStroke)
            .BorderThickness(1)
            .CornerRadius(4)
            .IsEnabled(!busy)
            .AutomationName(name is null ? "Select provider" : $"Selected provider: {name}")
            .Ref(selectorRef)
            .Set(button => button.HorizontalContentAlignment = HorizontalAlignment.Stretch);
    }

    private static ListViewElement BuildProviderList(
        CatalogProvider[] filtered,
        int selectedIndex,
        bool busy,
        Action<CatalogProvider> select,
        Action<CatalogProvider> pick,
        ElementRef listRef)
    {
        return ListView(
                selectedIndex < 0
                    ? Optional<int>.Unset
                    : Optional<int>.Of(selectedIndex),
                index =>
                {
                    if (index >= 0 && index < filtered.Length)
                    {
                        select(filtered[index]);
                    }
                },
                [.. filtered.Select(item => Body(item.Name).Padding(8, 8).WithKey(item.Id))])
            .SelectionMode(ListViewSelectionMode.Single)
            .ItemClick(
                index =>
                {
                    if (index >= 0 && index < filtered.Length)
                    {
                        pick(filtered[index]);
                    }
                })
            .Set(list => list.IsItemClickEnabled = true)
            .Ref(listRef)
            .IsEnabled(!busy)
            .AutomationName("Provider list");
    }

    private static TextBoxElement BuildSearchBox(
        string search,
        Action<string> setSearch,
        ProviderPickerProps props,
        CatalogProvider[] filtered,
        Action<CatalogProvider> pick,
        Action focusList,
        ElementRef searchRef)
    {
        return TextBox(
                search,
                setSearch,
                placeholderText: "Search providers")
            .SearchInput()
            .IsEnabled(!props.Busy)
            .AutomationName("Search providers")
            .Ref(searchRef)
            .OnKeyDown(
                (_, args) =>
                {
                    if (args.Key == VirtualKey.Down)
                    {
                        focusList();
                        args.Handled = true;
                        return;
                    }

                    if (args.Key == VirtualKey.Enter && filtered.Length > 0)
                    {
                        pick(filtered[0]);
                        args.Handled = true;
                    }
                });
    }

    private static CatalogProvider[] FilterProviders(ModelCatalog data, string query)
    {
        return [.. data.Providers
            .Where(
                candidate =>
                    query.Length == 0 ||
                    candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static Style CreatePickerPresenterStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 10000d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        return style;
    }
}
