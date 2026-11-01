---
type: build-and-style-conventions
title: Code Conventions and Build Constraints
description: The conventions a change must respect in Sebastian — warnings-as-errors with latest-all analyzers, AOT-safe code paths and the generated config binder, file-scoped experimental-API pragmas and justified per-member suppressions, internal-by-default types with InternalsVisibleTo for tests, primary constructors, source-generated LoggerMessage logging, and the `// shortcut:` markers that record decided tradeoffs.
tags: [conventions, build, analyzers, aot, trimming, logging, project-structure, code-style]
sources:
  - id: openwiki-source-d14cb4231320f903c4c26a78
    resource: repo://Program.cs
  - id: openwiki-source-3bf67c20a76eb7fa3cac2e6e
    resource: repo://Sebastian.csproj
  - id: openwiki-source-b5cf04fcd9c2585d8cde1706
    resource: repo://src/Agent/ImplementSpec.cs
  - id: openwiki-source-815a573c061c6efd30bc89a6
    resource: repo://src/Agent/ProviderResolver.cs
  - id: openwiki-source-12507fee3d1b69b9a31afb19
    resource: repo://src/Agent/Roles/AgentRole.cs
  - id: openwiki-source-12def67a8b093c0022f290e7
    resource: repo://src/Agent/Roles/ImplementationAgent.cs
  - id: openwiki-source-7709fb6d7a3503104984ea26
    resource: repo://src/Configuration/EnvironmentPlaceholders.cs
  - id: openwiki-source-c011dda0b85bc515c2bfb12c
    resource: repo://src/Configuration/SebastianConfig.cs
  - id: openwiki-source-4ebc3939bf12ec338e2fdd2b
    resource: repo://src/GitHub/GitHubService.cs
  - id: openwiki-source-4e824962a1990d68ad1f2c3d
    resource: repo://src/GitHub/Issues/SpecTemplateValidator.cs
  - id: openwiki-source-b3d60c1a960ba3aa23ae9a7a
    resource: repo://src/Jobs/IJob.cs
  - id: openwiki-source-167226a05ed7e6303a82d168
    resource: repo://src/Jobs/JobQueue.cs
  - id: openwiki-source-08195758b0bf04f6d257000b
    resource: repo://src/Libs/Git.cs
  - id: openwiki-source-a92f06976972996bb310dbe8
    resource: repo://src/Libs/RepositoryIdentity.cs
  - id: openwiki-source-c50ef9f06b01c87779c3983c
    resource: repo://src/Webhooks/GitHubWebhookProcessor.cs
  - id: openwiki-source-7f9ac1665b935b4f858f87e0
    resource: repo://tests/Sebastian.Tests/Jobs/JobQueueTests.cs
  - id: openwiki-source-c13b20e41fd15a696ab1eb84
    resource: repo://tests/Sebastian.Tests/Sebastian.Tests.csproj
generated: { by: "openwiki/0.7.2", at: "2026-11-01T14:54:06.427Z" }
verified:
  - by: openwiki/0.7.2
    at: 2026-11-01T14:54:06.427Z
---

# Code Conventions and Build Constraints

Sebastian's build is not a formality. The project compiles with `TreatWarningsAsErrors`, `AnalysisLevel` set to `latest-all`, code-style enforcement in the build, and `PublishAot`; the practical consequence is that **any new compiler or analyzer diagnostic is a build failure, not a suggestion**, and any code path that only works with a JIT and a reflection-heavy runtime is a build failure too. A change that "works locally" but introduces a `CA`/`IDE`/`IL` diagnostic does not merge, and the repository's own policy for that situation is stated in the project file: fix the underlying issue, or add a `NoWarn` for that specific rule with a reason.

This page covers the constraints and the conventions the existing code establishes for satisfying them. For what the app *does*, start with [System Overview and Runtime Topology](../architecture/overview.md); for how services are wired and bound, see [Startup, Composition, and Configuration Binding](../architecture/startup-and-composition.md).

## The build gate

```mermaid
flowchart TD
    CHANGE["Source change"] --> COMPILER["Compiler warnings and errors"]
    COMPILER --> ERRORS["TreatWarningsAsErrors promotes them to failures"]
    ERRORS --> CA["CA analyzers at AnalysisLevel latest-all"]
    CA --> IDE["Code-style rules enforced in build"]
    IDE --> AOT["AOT and trim analyzers because PublishAot is set"]
    AOT --> EXP["Experimental API diagnostics MAAI001 MEAI001 OPENAI001"]
    EXP --> OUT["Build result"]
    OUT --> FIX["Fix the code"]
    OUT --> SUPPRESS["Suppress one rule with a written justification"]
```

Every gate between a source change and a successful build, in the order a diagnostic usually surfaces.

| Gate | Setting | What it costs you if violated |
| --- | --- | --- |
| Warnings are errors | `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` | Any new warning or style diagnostic fails the build instead of scrolling past |
| Analyzer breadth | `AnalysisLevel` `latest-all` | Every analyzer the SDK ships is on, including rules the previous SDK version did not have |
| Native publishing | `PublishAot`, `IlcOptimizationPreference` `Size` | Reflection-based activation, binding, and serialization are reported by the trim/AOT analyzers |
| Configuration binding | `EnableConfigurationBindingGenerator` | Binding must go through the source-generated binder; the reflection binder is not AOT-safe |
| Experimental dependencies | `Microsoft.Agents.AI.*`, `Microsoft.Extensions.AI`, `OpenAI` channel types | Unannotated use of preview APIs produces `MAAI001`, `MEAI001`, and `OPENAI001` diagnostics |
| Globalization | `InvariantGlobalization` | Culture-sensitive formatting and parsing behave invariantly; ICU data is not available |

Two of these deserve emphasis because they surprise people arriving from JIT-era .NET:

**`latest-all` is a moving target.** The project file says so explicitly: *"latest-all: SDK upgrade can surface new analyzer errors; fix or justify a NoWarn per rule."* A routine SDK bump can therefore break the build with rules that had nothing to do with your change. The sanctioned responses are: fix the code, or add a narrowly scoped `NoWarn` naming the rule and why it does not apply here. Silencing a rule globally without a reason defeats the configuration, because `TreatWarningsAsErrors` is what makes the analyzer set load-bearing.

**AOT is treated as a property of the code, not a publish-time afterthought.** `PublishAot` turns on the trim and AOT analyzers, so reflective patterns are reported as diagnostics inside the same warnings-as-errors gate as everything else. The codebase contains **no** trim/AOT suppressions (`UnconditionalSuppressMessage`, `DynamicallyAccessedMembers`, `RequiresUnreferencedCode`) — AOT safety is achieved structurally instead, which is the pattern to preserve:

- Configuration binds through a source-generated path (`EnableConfigurationBindingGenerator`), so no runtime reflection over `SebastianConfig`.
- Logging is source-generated through `[LoggerMessage]` partial methods, so no reflection over format strings.
- Git is invoked by spawning the `git` binary with `ProcessStartInfo` and an argument list, not by a reflection-driven wrapper.
- GitHub and webhook traffic go through the generated Kiota/Octokit clients rather than hand-rolled serialization; repository code contains no direct `System.Text.Json` or `Activator` usage.
- Dependency injection constructs each service with an explicit factory lambda, so the container never has to reflect over an internal implementation type.

The binding constraint has a visible consequence for the config tree: `Sebastian.csproj` carries the comment *"Reflection binding is not AOT-safe; generated binder needs non-required properties (CS9035)"*, and every config property is therefore declared as `public string Foo { get; set; } = null!;` rather than as a `required` property. Converting those to `required` to clean up the null-forgiving operators breaks the build. Note the corollary for nullability: a `= null!;` initializer silences `CS8618` on that member *because* the binder is expected to supply it — it is not a claim that the value may be null at runtime. The key tree and validation rules are documented in [Startup, Composition, and Configuration Binding](../architecture/startup-and-composition.md).

## Suppression: three shapes, all justified

The repository suppresses diagnostics in exactly three spellings, chosen by scope. Every one of them names both the rule and the reason, and every one of them is narrow: nothing in the repository disables a category or a rule-wide analyzer wholesale, and no `<NoWarn>` entry has been added to either project file yet.

**File-level pragma for a whole experimental API surface**, at the top of the files that touch it, naming the rule and the reason:

```csharp
#pragma warning disable MAAI001 // HarnessAgent and file access APIs are experimental.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI experimental extensions.
```

`MAAI001`/`MEAI001` appear in the two role files (`AgentRole`, `ImplementationAgent`) and in `ImplementSpec` — the places that construct `HarnessAgent`, `LocalShellExecutor`, and file access. `ProviderResolver` instead suppresses `OPENAI001` and `MEAI001` for the OpenAI client surface. Keeping the pragmas at file scope, with the reason on the same line, is what makes it obvious that the experimental surface is contained to the agent layer rather than spread through the domain code. Two placement details are worth copying: in `ProviderResolver` the pragmas sit *after* the `namespace` statement (they apply to the file either way, so put them where the file reads cleanest), and `AgentRoleTests` in the test project carries the same MAAI001/MEAI001 pair even though the test project has no analyzer gate — the pragmas are there because the test touches the same preview surface and would otherwise be noisy. When you add a call to another preview API, expect a new diagnostic id and add it the same way; do not reuse an id that does not match the diagnostic you are silencing.

**Targeted attribute for a single member**, when a file-wide pragma would be too broad and the rule is about that one method or type:

```csharp
[SuppressMessage("Design", "CA1031", Justification = "A failed job must not tear down the queue.")]
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
```

That one is load-bearing behavior, not style: catching broadly inside the per-job body is how the queue keeps consuming after a job throws. The second instance is on the options type, with a comment above it repeating the reason:

```csharp
// CA1812: options types are created by the options infrastructure, not by visible `new`.
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by the options infrastructure.")]
internal sealed class SebastianConfig
```

Note that these two are the *entire* `SuppressMessage` population of the repository. There are only two, each one attributable to a specific structural fact (an overridden `BackgroundService` method and a container-constructed options type) rather than to convenience. If a third starts to feel necessary, that is a signal to check whether the code can be restructured instead.

**A narrow compiler-warning pragma is not an option of last resort either, but it keeps the same discipline.** The only compiler-level silencer in `src/` is the `#pragma warning disable` family above; everything else is fixed. This means there is **no precedent for `#pragma warning disable CS0618`, `CS1591`, or nullable warnings** — the established answer for a compiler warning is to change the code.

The convention to copy: `SuppressMessage` carries a category, a rule id, and a `Justification` sentence that explains why the analyzer is wrong *here*. An empty or vague justification is as good as no suppression at all, because the next reader cannot tell whether it is still true. And a suppression is a precedent someone will follow: the fact that only two exist is what keeps a third from looking routine.

## Logging is always source-generated

There is no `logger.LogInformation(...)` call anywhere in the repository. Every log statement is a `partial` method decorated with `[LoggerMessage]`, and every containing class is declared `partial` to host the generated implementation:

```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Implementation completed for {Repository}#{IssueNumber} at {Path}. Git status: {Status}. Agent response: {Response}")]
private static partial void LogCompleted(ILogger logger, string repository, int issueNumber, string path, string status, string? response);
```

Three details of the established pattern:

- **The declaring type must be `partial`.** `JobQueue`, `Git`, `GitHubWebhookProcessor`, and `ImplementSpec` are all `internal sealed partial class`. Adding a log line to a non-partial class means making it partial first.
- **A `static` logger parameter is used when the logger is not a field.** `ImplementSpec` is a job object with no logger field, so `LogCompleted` takes `ILogger logger` explicitly and is `static`; `Git` and `JobQueue` hold their logger and declare instance partial methods instead.
- **Levels and message templates live in the attribute**, so the level is visible at the call site in source and structured property names are checked by the generator. Exceptions are passed as a first parameter (`JobQueue.LogFailed(Exception exception, string key)`, `Git.LogSyncFailed(Exception exception)`), which is how the generator wires up exception logging.

One consequence of the split that is easy to trip over: an instance `[LoggerMessage]` method can only be declared on a `partial` class that has the logger in scope, so the four `partial` types above are `partial` *for logging* — `ProviderResolver`, `GitHubService`, `ImplementationAgent`, and the config types are not, because they log nothing. A class that starts logging becomes `partial` at that moment.

## Type and member conventions

**Internal by default, `sealed` unless designed for inheritance.** Every service and helper in `src/` is `internal sealed` — including `RepositoryIdentity`, which is a `record` — with two intentional extension seams left unsealed: `AgentRole` is `internal abstract` (roles are derived) and `IJob` is `internal interface` (jobs are implemented). The only exceptions to the `sealed` half are `EnvironmentPlaceholders` and `SpecTemplateValidator`, which are `internal static` utility classes and cannot be sealed. `Program.cs` is not a type at all — top-level statements.

**Tests reach internals through the project file, not through `public`.** `Sebastian.csproj` declares `<InternalsVisibleTo Include="Sebastian.Tests" />` and the test project holds a `ProjectReference` to the web project. That is why an `internal` method like `AgentRole.CreateOptions()` or `ImplementSpec.PrepareWorkspaceAsync` can be asserted directly, and why making something `public` "so the tests can see it" is the wrong move.

**Primary constructors when the constructor only captures parameters.** Classes whose dependencies are pure state use the parameter-list form:

```csharp
internal sealed class ImplementationAgent(
    string name,
    string workspacePath) : AgentRole, IAsyncDisposable
```

`JobQueue`, `ImplementSpec`, and `GitHubWebhookProcessor` follow the same shape — note that even `JobQueue` and `GitHubWebhookProcessor`, which *assign* their captured parameters into readonly fields or use them directly, stay with the primary constructor form. Classes that do real work before the object is usable keep an explicit constructor instead — `Git` normalizes the content root and stores a logger, `ProviderResolver` validates the provider configuration and resolves the endpoint, `GitHubService` imports the PEM and builds the Kiota adapter. The split is visible in the same codebase, and it is the signal to use: primary constructor for capture, explicit constructor for validation or field initialization that can throw.

**Factory registrations instead of implementation-type registrations** where the container would otherwise reflect over an internal type. `Program.cs` carries the reason inline:

```csharp
// Factory, not AddSingleton<TService, TImpl>: internal processor is unknown to DI's reflection path.
builder.Services.AddSingleton<WebhookEventProcessor>(
    services => new GitHubWebhookProcessor(
        services.GetRequiredService<ILogger<GitHubWebhookProcessor>>(),
        ...
```

The convention is applied consistently, not selectively: `GitHubService`, `ProviderResolver`, `Git`, and `JobQueue` are all registered the same way, and the two hosted services (`Git`, `JobQueue`) are registered twice — once as singletons, once via `AddHostedService(services => services.GetRequiredService<T>())` — so the webhook processor and the background loop share one instance. `Program.cs` also uses `_ =` discards for the eager resolutions that exist only to fail fast.

**Small mechanical conventions that appear consistently:** collection expressions (`[new ShellEnvironmentProvider(_shell)]`, `[..]` for tool lists, array returns from `SpecTemplateValidator.FindMissingSections`); `Path.Combine` plus `Path.TrimEndingDirectorySeparator` rather than string concatenation of paths; an explicit `StringComparison` argument on essentially every string comparison (`Ordinal`, or `OrdinalIgnoreCase` for labels and provider names, with `OperatingSystem.IsWindows()` selecting between them in `Git.PathEquals`); `CultureInfo.InvariantCulture` for number parse and format (`Git.CountCommitsAsync`, `ImplementSpec`'s issue-number directory name), which is the direct consequence of `InvariantGlobalization`; `ConfigureAwait(false)` on every `await` (verified across `src/`), `await using` included; and `_ =` discards (`_ = PrepareLock.Release();`) for results that are deliberately ignored. LINQ is used freely (`Where`, `Any`, `FirstOrDefault`, `GroupBy`) and is AOT-safe; what is avoided is *binding-time* reflection, not generic abstractions.

## Comments and `// shortcut:`

There is no XML documentation anywhere in the repository — not one `///` comment — and no `TODO`. Comments explain *why* a line is the way it is, and they appear where the alternative would look like a mistake:

```csharp
// One repository: serialize fetch, fast-forward, and worktree changes.
private static readonly SemaphoreSlim PrepareLock = new(1, 1);

// Reuse it: the previous run may hold uncommitted agent work that must not be deleted.
if (Directory.Exists(worktreePath))
```

`// shortcut:` is a distinct marker: it labels a **known pragmatic tradeoff that has been decided, not a defect waiting to be reported**. There are four, and each names both the shortcut and the condition that would justify upgrading it:

- `Git.StartAsync` — *"shortcut: push + startup sync only; add a periodic fetch if missed push webhooks become a problem."* The startup sync is best-effort and bounded to ten seconds for the same reason, stated in a plain comment on the line above.
- `Git.SyncMainAsync` — *"shortcut: worktree prep can run during a fast-forward; worst case the prep fails on git's index.lock, the repo stays consistent."* The race is accepted because the failure mode is a failed preparation, not corruption.
- `GitHubWebhookProcessor.ProcessPushWebhookAsync` — *"shortcut: synchronous fetch in the webhook; move to a background queue if delivery latency becomes a problem."* Push handling blocks the HTTP response on purpose.
- `EnvironmentPlaceholders.ExpandEnvironmentPlaceholders` — *"shortcut: whole-value placeholders only; extend if inline interpolation is needed."* A value must be exactly `{env:NAME}`; a setting that mixes literal text and a placeholder is passed through untouched.

Treat these as documented decisions. Removing the shortcut is a design change that should replace the comment with the new arrangement; "fixing" it silently, or deleting the comment while leaving the behavior, destroys the only record of why the code looks wrong. The same instinct applies to `SpecTemplateValidator`'s *"Keep in sync with skills/to-spec/SKILL.md"* comment, which marks a cross-file duplication that no test enforces.

## Project layout constraints

There is no solution file and no `Directory.Build.props`: `dotnet build Sebastian.csproj` and `dotnet test tests/Sebastian.Tests/Sebastian.Tests.csproj` are the entry points, and all shared build settings live in the one web project file.

The test project is deliberately configured *less* strictly than the product: it inherits none of `TreatWarningsAsErrors`, `AnalysisLevel`, `PublishAot`, or `InvariantGlobalization`, and it adds only the xunit stack, a global `<Using Include="Xunit" />` (which is why test files have no `using Xunit;`), and `IsPackable` `false`. The web project excludes the test tree from compilation with `<DefaultItemExcludes>$(DefaultItemExcludes);tests/**</DefaultItemExcludes>`, so the web project never compiles a second copy of the tests.

Three consequences for a change:

- **`tests/**` is outside the product compilation, so a green `dotnet test` proves neither analyzer nor AOT cleanliness.** The tests are compiled only by the test project, under a different (looser) rule set, and never by the project that carries `PublishAot` and `latest-all`. The product build is the stricter gate; run `dotnet build Sebastian.csproj` before believing a change is clean.
- **A diagnostic that fails the build in `src/` may pass in `tests/`.** Worth fixing anyway, but it will not be the thing that stops you — and it will not be caught until someone moves the code.
- **Code that must be AOT-clean cannot be validated from the test project.** Analyzer evidence comes from the web project only, and the test project's `ProjectReference` does not make the test sources part of that analysis.

Test classes are `public sealed` and the two helper types (`TestHostEnvironment`, `TestRepository`) are `internal sealed` — the inverse of the production convention, relaxed exactly where the xunit discovery mechanism requires it.

Test conventions and coverage gaps are covered in [Testing Strategy and Harness](../testing/test-strategy.md); the agent-layer use of the experimental pragmas is covered in [Agent Roles, Tools, and Sandbox Boundaries](agent-execution.md).

## Change checklist

- **New analyzer diagnostic?** Fix it. If it genuinely does not apply, add a `NoWarn` for that rule *per rule* with a reason, or a member-scoped `SuppressMessage` with `Justification` — never a blanket disable.
- **New configurable setting?** A bindable, non-`required` property with a `= null!;` initializer, plus a validation attribute if mandatory, plus a consumer reading `IOptions<SebastianConfig>`. Do not reach for a reflection-based binder or a custom `TypeConverter`.
- **New preview API call?** Expect a new experimental diagnostic id; scope the pragma to the file that touches the harness surface, with the reason on the same line.
- **New log line?** A `[LoggerMessage]` partial method on a `partial` class. No `ILogger.Log*` calls.
- **New service?** `internal sealed`, registered with a factory if it takes dependencies you want pinned; internal visibility is enough for the tests, and a static utility class is `internal static`.
- **Adding a test?** It may use internals freely; it must not assume the production warnings-as-errors or AOT analysis apply to it.
- **Tempted to simplify an odd-looking block?** Check for a comment or a `// shortcut:` first — the oddity is usually the decision.
