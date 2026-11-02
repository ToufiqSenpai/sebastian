namespace Sebastian.Windows.Pages;

internal abstract record Route
{
    internal sealed record Home : Route;
    internal sealed record Kanban : Route;
    internal sealed record Project(string Name) : Route;
    internal sealed record Settings : Route;
    internal sealed record Setup : Route;
}
