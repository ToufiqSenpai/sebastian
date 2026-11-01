# Sebastian.Windows Agent Instructions

This file scopes the rules for the `Sebastian.Windows` app. The repository root`AGENTS.md` still applies.

## Mandatory: research Reactor before changing code

This is a Microsoft.UI.Reactor app. Before touching UI, navigation, state,styling, windowing, or app bootstrap code, you **MUST** read the relevant upstream Reactor documentation. Do not infer Reactor APIs from WinUI/XAML or generic C# patterns.

1. **Reactor repository guide**
   <https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs>

   Read the pages that match the task. At minimum:

   - `getting-started.md` — app bootstrap and project structure
   - `components.md` — pages, components, props, composition
   - `hooks.md` — state, effects, controlled inputs
   - `navigation.md` — `UseNavigation`, `NavigationHost`, `NavigationView`
   - `rules-of-reactor.md` — hook order, render purity, keys
   - `windowing.md` / `packaging.md` — title bar, window, build changes

2. **Agent kit shipped with the pinned NuGet package**

   `%USERPROFILE%\.nuget\packages\microsoft.ui.reactor\<version>\agentkit\`

   Use the version from `Sebastian.Windows.csproj`, then consult:

   - `SKILL.md` — framework rules and consumer setup
   - `reactor.api.txt` — authoritative API signatures for that version
   - `skills\` — topic guides such as `design.md`, `forms.md`, `navigation.md`, and `recipes\sidebar-nav.cs`

3. If the docs and the pinned package disagree, the pinned API index and XML docs win. Do not invent an API; inspect the package or Reactor source when still uncertain.

Include the documentation pages you used in your change summary. A UI change made without checking these docs is incomplete.

## Project conventions

- Pure C# Reactor: no `.xaml`, no code-behind, no ViewModels, no data binding.
- Pages live in `Pages/` under `Sebastian.Windows.Pages`; typed routes live in `Pages/Route.cs`.

## Run

Development **must** use watch mode. Reactor's hot reload only runs under
`dotnet watch`; a plain `dotnet run` gives no reload loop.

```powershell
cd Sebastian.Windows
$env:Platform = "x64"
$env:WinAppRunUseExecutionAlias = "true"
dotnet watch run
```

`WinAppRunUseExecutionAlias=true` is required: this is a packaged app, and without it
the WinApp launcher activates it by AUMID, which drops the environment `dotnet watch`
needs to load its hot-reload agent.

Hot reload does **not** work reliably in this packaged setup: after a save,
`dotnet watch` prints "C# and Razor changes applied", but the running window keeps the
old UI — observed on `App.cs` and `Pages/HomePage.cs`, and navigating away and back does
not help. Restart instead: stop the watch process and start it again (or press `Ctrl+R`
in the watch terminal), then verify the window actually changed. Do not trust the watch
log as proof a change landed.

Outside development, a one-off run without hot reload is fine:

```powershell
dotnet run --project Sebastian.Windows -p:Platform=x64
```
