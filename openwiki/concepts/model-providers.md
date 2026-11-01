---
type: model-provider-resolution
title: Model Provider Resolution
description: How Sebastian turns configuration into one LLM — the per-element options validation of Agent.Providers, the cross-field rules (non-empty list, case-insensitive unique names, selection, endpoint shape) enforced in ProviderResolver's constructor, the default endpoint https://api.openai.com/v1, and why a fresh IChatClient is created per job and disposed by the run.
tags: [providers, configuration, llm, chat-client, startup-validation, failure-modes, lifecycle]
sources:
  - id: openwiki-source-ede59da55b0d48006223d1c0
    resource: repo://appsettings.json
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
  - id: openwiki-source-7709fb6d7a3503104984ea26
    resource: repo://src/Configuration/EnvironmentPlaceholders.cs
  - id: openwiki-source-c011dda0b85bc515c2bfb12c
    resource: repo://src/Configuration/SebastianConfig.cs
  - id: openwiki-source-167226a05ed7e6303a82d168
    resource: repo://src/Jobs/JobQueue.cs
  - id: openwiki-source-5b02b033021548a1c1fc0ba0
    resource: repo://tests/Sebastian.Tests/Agent/ProviderResolverTests.cs
generated: { by: "openwiki/0.7.2", at: "2026-11-01T14:54:06.427Z" }
verified:
  - by: openwiki/0.7.2
    at: 2026-11-01T14:54:06.427Z
---

# Model Provider Resolution

`src/Agent/ProviderResolver.cs` is the single place where configuration becomes a model. It is small — one explicit constructor, one factory method, three private fields — but it carries three separable responsibilities that are worth keeping apart:

1. **Admission** — is the provider list itself coherent (non-empty, uniquely named, containing the one entry the operator selected)?
2. **Selection** — resolve *which* provider, *which* endpoint, and *which* model id, exactly once, at startup.
3. **Client construction** — build an `IChatClient` on demand, once per job, from the already-resolved values.

Responsibilities 1 and 2 happen in the constructor and therefore at process startup; responsibility 3 happens inside a job. Splitting them this way is deliberate: a misconfigured provider list should stop the process from serving webhooks, while a wrong-but-well-formed API key should only fail the run that used it.

```csharp
internal sealed class ProviderResolver
{
    private const string DefaultEndpoint = "https://api.openai.com/v1";

    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string _model;

    public ProviderResolver(IOptions<SebastianConfig> options) { /* validation + resolution */ }

    public IChatClient CreateChatClient() { /* new OpenAI client, per call */ }
}
```

Nothing else in the repository reads provider configuration. `ImplementSpec` is the only caller of `CreateChatClient`, and no other type touches `Agent:Providers` or `Agent:General:Model`. See [Agent Roles, Tools, and Sandbox Boundaries](agent-execution.md) for what happens to the returned client, and [Startup, Composition, and Configuration Binding](../architecture/startup-and-composition.md) for how the resolver gets its options.

## The configuration surface

The keys are the property names on the config tree in `src/Configuration/SebastianConfig.cs`; a `List<ProviderConfig>` binds by numeric index, so the list members are `Agent:Providers:0:Name`, `Agent:Providers:1:ApiKey`, and so on (`__` instead of `:` in environment variables).

| Key | Type / rule | Consequence |
| --- | --- | --- |
| `Agent:General:Provider` | `string`, `[Required]` | Selection key. Matched case-insensitively against provider `Name`; it is **not** an index or a URL |
| `Agent:General:Model` | `string`, `[Required]` | Passed verbatim to `GetChatClient(_model)`. Single-valued for the whole process |
| `Agent:Providers:<i>:Name` | `string`, `[Required]` | Identity used for selection and the uniqueness check |
| `Agent:Providers:<i>:Endpoint` | `string?`, optional, no attribute | Base URL for the OpenAI-shaped client. `null` → `https://api.openai.com/v1` |
| `Agent:Providers:<i>:ApiKey` | `string`, `[Required]` | Becomes an `ApiKeyCredential`. Never logged, never re-read after startup |

`Agent` carries `[ValidateObjectMembers]` and `Agent.Providers` carries `[ValidateEnumeratedItems]`, so **every** element of the list is validated, not just the selected one. Practically: a second provider entry kept "for later" still needs a non-blank `Name` and `ApiKey`, or the process refuses to start. Its `Endpoint`, however, is never URI-checked unless it is the selected entry — that check lives in `ProviderResolver`, not in the config attributes.

The shipped `appsettings.json` follows the same shape with a non-OpenAI vendor already wired in: `Agent:General:Provider` is `deepseek`, `Model` is `deepseek-v4.1-flash`, and the single entry carries `Endpoint` `https://api.deepseek.com/v1` with `ApiKey` supplied as the `{env:DEEPSEEK_API_KEY}` placeholder. The endpoint is a literal because it is not a secret; the key is a placeholder because it is one — the placeholder expansion happens at `builder.Configuration` time, before binding, so what binds is either a real key or a startup failure (see [Startup, Composition, and Configuration Binding](../architecture/startup-and-composition.md)).

`Endpoint` being `string?` interacts with the default in a way that is easy to get wrong: the expression is `selected.Endpoint ?? DefaultEndpoint`, which only falls back on `null`. An explicitly empty string is not `null`, so it reaches `Uri.TryCreate` and fails as an invalid endpoint rather than silently using the OpenAI default — unless the configuration provider in play normalizes `""` to `null` before binding (as `AddEnvironmentVariables` does), in which case an empty environment override of the endpoint would quietly select the OpenAI default instead. Whether an empty endpoint errors or defaults therefore depends on where the empty value came from, not on the resolver.

## Two layers of validation, split by kind

There are two distinct places where a bad provider configuration is caught, and they catch different *kinds* of mistake.

**Schema-level (options validator).** `[OptionsValidator] partial class ValidateSebastianConfig : IValidateOptions<SebastianConfig>` generates per-member checks from the data annotations and is registered in `Program.cs` next to `.Bind(...).ValidateOnStart()`. It knows only about individual members: required and non-blank, in range, and — through `[ValidateObjectMembers]` / `[ValidateEnumeratedItems]` — recursion into nested objects and list items. It cannot see relationships *between* members.

**Cross-field (resolver constructor).** The four rules that involve more than one value — the list is non-empty, no two names collide, the selected name exists, and the selected endpoint parses — cannot be expressed as annotations on one property, so they live in `ProviderResolver`'s constructor, which calls `options.Value` as its first statement.

That `options.Value` call is the hinge: reading `Value` runs the registered `IValidateOptions` implementations, so the options validator always fires *first*, and the constructor-level checks only run on a config that already passed schema validation. This is why the two layers cooperate rather than duplicate: a whitespace-only `ApiKey` never reaches the resolver at all.

### Why the constructor checks are a startup failure, not a first-job failure

`Program.cs` registers the resolver as a singleton through an explicit factory:

```csharp
builder.Services.AddSingleton<ProviderResolver>(
    services => new ProviderResolver(services.GetRequiredService<IOptions<SebastianConfig>>()));
```

and then, immediately after `Build()` and before `MapGitHubWebhooks` and `RunAsync`, resolves it eagerly:

```csharp
var config = app.Services.GetRequiredService<IOptions<SebastianConfig>>().Value;

// Construct the services now so missing credentials fail at startup.
_ = app.Services.GetRequiredService<GitHubService>();
_ = app.Services.GetRequiredService<ProviderResolver>();
```

A singleton is otherwise only constructed on first request, and this process has no request for a model on the webhook path — the model is used inside background jobs. Without the eager line, a provider-list error would surface minutes later as a failed job. With it, construction *is* the startup gate: every constructor throw aborts the host before it starts listening. The same line also means the options validator cannot be deferred either — `Program.cs` reads `IOptions<SebastianConfig>.Value` above the eager resolutions, so schema failures precede resolver failures in every case.

## The resolution ladder

The constructor's checks run in a fixed order, and the order is part of the contract (it is what the tests pattern-match on): an operator fixing configuration sees the *first* problem only.

```mermaid
flowchart TD
    START["ProviderResolver ctor calls IOptions.Value<br/>(runs ValidateSebastianConfig first)"] --> EMPTY{"Providers list empty"}
    EMPTY -->|yes| F1["throw: At least one agent provider must be configured"]
    EMPTY -->|no| DUP{"any Name repeated, case-insensitive"}
    DUP -->|yes| F2["throw: Agent provider name configured more than once"]
    DUP -->|no| SEL{"entry matching Agent.General.Provider exists"}
    SEL -->|no| F3["throw: Agent provider is not configured"]
    SEL -->|yes| URI{"Endpoint or default parses as an absolute URI"}
    URI -->|no| F4["throw: Agent provider has an invalid endpoint"]
    URI -->|yes| OK["store apiKey, endpoint, model - construction succeeds"]
```

Constructor validation order: list non-empty, then name uniqueness, then selection, then endpoint shape. Only the last step touches the selected entry.

Three details in that ladder are load-bearing:

- **The duplicate check runs before selection**, and it groups with `StringComparer.OrdinalIgnoreCase`. Two entries named `DeepSeek` and `deepseek` are a startup failure even if the selected entry is unambiguous — the resolver refuses to leave a shadowed entry in place. The message interpolates the duplicate group's key, i.e. the first spelling encountered.
- **Selection is case-insensitive** through `string.Equals(provider.Name, agent.General.Provider, StringComparison.OrdinalIgnoreCase)`, and takes the *first* match. Combined with the preceding uniqueness check, "first match" can never be ambiguous; the two checks are what make an `OrdinalIgnoreCase` lookup safe. A missing match is expressed as a null-coalescing throw on the `FirstOrDefault` result, so the failure carries `Agent.General.Provider` (what the operator asked for), not a provider name.
- **Endpoint validation is shape-only.** `Uri.TryCreate(value, UriKind.Absolute, ...)` accepts any absolute URI — `ftp://host`, `file:///x` — because no scheme or HTTP check exists. A syntactically valid but useless endpoint passes startup and fails at request time.

Once constructed, the resolver holds `_apiKey`, `_endpoint`, and `_model` in fields read from the selected entry and from `Agent.General`. There is no setter, no `IOptionsMonitor`, and no re-read: the model identity is fixed for the process lifetime, and changing it requires editing configuration and restarting.

## Failure modes: exact messages and when they fire

All four failures below are `InvalidOperationException` thrown from the constructor. Because of the eager resolution shown above, every one of them aborts startup rather than surfacing on the first webhook.

| Failure | Message | Fires at |
| --- | --- | --- |
| A required member is null/blank (e.g. `ApiKey`, `Name`, `Provider`, `Model`) | `OptionsValidationException` from the generated validator, naming the failed member — never the resolver's messages | Reading `IOptions.Value` — before the constructor body runs |
| `Agent:Providers` binds to an empty list | `At least one agent provider must be configured.` | Construction — startup |
| Two entries share a `Name` (case-insensitive) | `Agent provider '<name>' is configured more than once.` | Construction — startup |
| `Agent:General:Provider` matches no entry `Name` | `Agent provider '<name>' is not configured.` | Construction — startup |
| `Endpoint` present but not an absolute URI (includes the empty-string case from JSON) | `Agent provider '<name>' has an invalid endpoint.` | Construction — startup |
| Wrong/missing/revoked API key, unknown model id, unreachable host | no message from this class — the provider SDK's error | First agent run, caught by `JobQueue` and logged as `Job {Key} failed` |

The last row is the deliberate gap. `ProviderResolver`'s constructor does not contact the network and `CreateChatClient` does not either, so **credential validity and model existence are unproven until the first run actually talks to the endpoint**. That is a startup-versus-first-job tradeoff, not an oversight; the eager resolution exists to catch *shape* errors cheaply. Where the resulting exception ends up is described in [Job Queue and Execution Semantics](job-queue.md).

### The validator fires before the constructor does

Blank-string rules are not the resolver's job. `[Required]` on a `string` rejects null, empty, and whitespace-only values, and the source-generated `ValidateSebastianConfig` enforces it. Because the constructor's first statement dereferences `options.Value`, the options infrastructure runs the validators at that moment — and `Program.cs` has already read `.Value` for the webhook secret before the eager resolutions, so validation always precedes `ProviderResolver`.

`ProviderResolverTests.BlankApiKeyIsRejectedByGeneratedValidator` pins exactly this boundary: it validates a fully populated config (`valid.Failed == false`), sets `config.Agent.Providers[0].ApiKey = " "`, and asserts `invalid.Failed == true`. The evidence value is the ordering — a whitespace-only key never reaches the resolver, it is rejected on the `.Value` read. This matters when adding settings: values that are required but not structurally checked (like `Model`) need an attribute, because no code in `ProviderResolver` examines their content.

## CreateChatClient: one client per job

```csharp
public IChatClient CreateChatClient()
{
    var client = new OpenAIClient(
        new ApiKeyCredential(_apiKey),
        new OpenAIClientOptions { Endpoint = _endpoint });

    return client.GetChatClient(_model).AsIChatClient();
}
```

The method is a pure factory over the three captured fields: no parameters, no caching, no reuse. Every call allocates a new `OpenAIClient`, a new chat client for `_model`, and a new `IChatClient` adapter over it.

```mermaid
sequenceDiagram
    participant JOB as ImplementSpec.RunAsync
    participant PR as ProviderResolver singleton
    participant SDK as OpenAI client stack
    participant ROLE as AgentRole
    JOB->>JOB: PrepareWorkspaceAsync (git fetch, worktree)
    JOB->>PR: CreateChatClient()
    PR->>SDK: new OpenAIClient(ApiKeyCredential, Endpoint)
    PR->>SDK: GetChatClient(model).AsIChatClient()
    PR-->>JOB: IChatClient
    JOB->>ROLE: role.CreateAgent(chatClient)
    ROLE-->>JOB: HarnessAgent
    JOB->>SDK: RunAsync - model call happens here
    JOB->>JOB: finally - dispose if IDisposable
```

Per-job client creation, and the model call that can first reveal a bad credential happening inside the run, not at startup.

This is the current model, and it has consequences worth stating plainly:

- **`ProviderResolver` is a singleton but `IChatClient` is not.** The selection is process-wide; the client is per-run. `ImplementSpec.RunAsync` calls `CreateChatClient()` after preparing the workspace, so a job that fails during workspace preparation never allocates a client at all.
- **No resilience knobs.** `CreateChatClient` sets only `Endpoint`. There is no retry, timeout, delegation handler, proxy, or custom `HttpClient`, and no logging hook — adding any of those means adding `OpenAIClientOptions` here, which affects every agent role at once.
- **No per-role or per-job model selection.** `_model` comes from one scalar (`Agent:General:Model`). A second role that wants a different model, or a cheap model for triage and an expensive one for implementation, cannot be expressed in configuration today; it needs a signature change here (for example `CreateChatClient(string model)`) or a second resolver. Likewise, a provider entry can only override the base URL — there is no way to configure headers, an auth scheme, or an API version.
- **The API key lives for the process lifetime** in a singleton field, even though it is only used when a job builds a client. It is never logged anywhere in the codebase, which is the property to preserve.
- **Vendor-neutrality is assumed, not enforced.** The provider list is a list of *names*; each entry only supplies a URL and a key, and the OpenAI client stack talks to all of them. The shipped DeepSeek configuration is the working proof that a non-OpenAI base URL is intended to work; nothing in the code proves a given vendor accepts the `/v1` path shape or these model ids.

### The disposal contract is defensive, not guaranteed

`IChatClient` does not promise `IDisposable`, and this code does not assume it does. The caller guards:

```csharp
finally
{
    if (chatClient is IDisposable disposable)
    {
        disposable.Dispose();
    }
}
```

`ImplementSpec` disposes the client in a `finally` nested inside the `await using` on the role, so the client is released before the role (and its shell) is torn down, and it is released on exception too. The test carries the same `(client as IDisposable)?.Dispose()` shape, which is a hint about intent rather than a contract. Two things are *not* established by the code: whether the adapter actually propagates disposal to the `OpenAIClient`'s HTTP pipeline, and how long that pipeline survives. There is no test asserting that disposal reaches anything, so treat "client is disposed" as "the disposable handle returned by `CreateChatClient` is disposed", and nothing stronger. The enclosing role's `IAsyncDisposable` handles the shell; the two lifetimes are separate, which is why the `finally` sits where it does.

## What is *not* validated — a checklist before blaming the provider

Reading the code as an exhaustive validation layer will mislead you. The resolver proves only that the configuration *has a shape* it can hand to the OpenAI SDK:

- **Nothing vendor-specific is known.** The code proves an API key plus a base-URL override plus a model id, using the OpenAI client stack. It does not prove that a given vendor accepts that URL shape, that its model ids are free-form, or that any particular vendor's `/v1` path is correct. The test fixture uses `https://api.deepseek.com/v1` with model `deepseek-v4.1-flash`, which demonstrates that a non-OpenAI base URL is *intended* to work — not that it does.
- **Provider names are free-form labels.** `Name` is only a selection key; nothing maps a name to a URL, a header, or a capability.
- **Non-selected providers are inert.** Their API keys are validated as non-blank but never read, and their endpoints are never parsed. Keeping an unused entry costs a required secret and nothing else.
- **No cost, rate, or quota control exists** at this layer. The PRD's per-run and per-day cost bounds are not implemented in the provider path.

## Changing this safely

- **Adding a setting** means three coordinated edits: a bindable property on the config tree, a validation attribute if it is mandatory, and a read in `ProviderResolver` (or its caller). Prefer adding the check to the config attributes when possible — it then fires on `.Value`, earlier than the constructor.
- **A rule spanning two members belongs in the constructor**, not in an annotation. If you add one, append it after the endpoint check rather than reordering the existing four; the tests match on messages, and the order is what makes the first reported problem predictable.
- **Adding a per-vendor requirement** (custom headers, a different auth scheme, an API-version query parameter) is a change to `CreateChatClient`, because that is the only place an SDK options object exists. Do the work there rather than sprinkling provider branches into callers.
- **Making a client failure fail faster** is a tradeoff, not a free win: a probe call in the constructor or in the eager block would convert first-run failures into startup failures, but would also make startup depend on network reachability, which the current design explicitly avoids.
- **Tightening endpoint validation** (require an `http`/`https` scheme) is a small, safe change that converts a request-time failure into a startup failure and is not currently pinned by any test.
- **Never log the resolved `_apiKey`**, and do not add code that re-reads it per job — one read at startup is what keeps the secret surface small.

## What the tests pin, and what they leave open

`tests/Sebastian.Tests/Agent/ProviderResolverTests.cs` builds a resolver directly with `Options.Create(config)` — never through DI, so the eager-resolution wiring in `Program.cs` is *not* under test — and never reaches the network:

- `MissingProviderFails`, `DuplicateProviderNamesFail`, and `EmptyProvidersFails` each construct with a deliberately broken config and assert the exception type and a message fragment (`"missing"`, `"more than once"`, `"At least one agent provider"`). A reordered or removed check becomes a visible break rather than a startup surprise. Note `DuplicateProviderNamesFail` adds a second entry spelled exactly `deepseek` — the same casing — so it pins the message, not the `OrdinalIgnoreCase` grouping.
- `SelectedProviderCreatesChatClient` asserts only `NotNull` on the result and disposes it defensively. Its real value is negative evidence: the whole test suite constructs the resolver and a chat client with no network available, which only works because neither validates reachability.
- `BlankApiKeyIsRejectedByGeneratedValidator` pins the validator-versus-constructor split described above, using `ValidateSebastianConfig()` directly rather than going through `.Value`.

Left unpinned: the default endpoint constant, the `has an invalid endpoint` message, the case-insensitive selection path, the model id actually passed to `GetChatClient`, and any assertion that disposal propagates. Changes to any of those are unverified by the suite — see [Startup, Composition, and Configuration Binding](../architecture/startup-and-composition.md) for the full fail-fast inventory and [Running, Configuration, and Deployment](../operations/running-and-deployment.md) for the operator checklist that consumes these messages.

## Related

- [Startup, Composition, and Configuration Binding](../architecture/startup-and-composition.md) — why the resolver is a singleton resolved eagerly, placeholders, and the whole fail-fast table.
- [Agent Roles, Tools, and Sandbox Boundaries](agent-execution.md) — what the `IChatClient` is wrapped into, and the exact disposal ordering inside a run.
- [Job Queue and Execution Semantics](job-queue.md) — where a request-time model failure ends up (per-job catch and log).
- [Running, Configuration, and Deployment](../operations/running-and-deployment.md) — the operator view of the keys and the startup failure texts.
