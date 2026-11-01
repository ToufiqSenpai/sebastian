namespace Sebastian.Windows.Pages;

abstract record Route
{
    public sealed record Home : Route;
    public sealed record Kanban : Route;
    public sealed record Project(string Name) : Route;
    public sealed record Settings : Route;
}
