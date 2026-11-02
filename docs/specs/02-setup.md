# Specification 02 — Setup, Projects, and Models

Status: draft for review
Date: 2026-11-01
Source: `docs/intent.md`
Depends on: [01-foundation](01-foundation.md)
Related: [03-chat-and-spec](03-chat-and-spec.md), [04-build-and-review](04-build-and-review.md), [05-board](05-board.md)

## 1. Problem

The current app reads its GitHub App credentials and provider settings from a configuration file and environment placeholders. There is no first-run experience, no way to add a project, no provider catalog, and no way to prove a credential or model works before the board depends on it. The task model in `01-foundation` also assumes one repository per process, while the shell already has a Projects navigation area and the product needs more than one project.

This spec defines the global setup wizard, the provider and model catalog, the decision-model configuration, the project registry, and the project runtime that makes several projects coexist in one process. It is the contract `03`, `04`, and `05` build on.

## 2. Scope

In scope:

- A blocking first-run wizard for the global credentials and models.
- A mandatory test gate for every credential and model before it is saved.
- The models.dev provider and model catalog, cached on disk and revalidated with its ETag.
- The two supported chat adapters: OpenAI-compatible and Anthropic.
- The decision model: two wire formats, live model listing, a probe, and storage. There is no consumer in v1.
- The project registry, the add-project flow, per-project runtimes, and webhook routing by repository.
- The global and per-project settings keys added to `01-foundation`.

Out of scope here: the chat turn contract (`03`), build and review behaviour (`04`), and the board and settings layout (`05`).

## 3. Terms

| Term | Meaning |
|---|---|
| Global settings | Values shared by every project: GitHub App, PAT, providers, models. Stored once in `KeyValueStore`. |
| Project | One local git top-level folder with its own `.sebastian/sebastian.db`, chat session, tasks, scheduler, and PR flow. Identified by a UUIDv7. |
| Active project | The project the Home, Board, and Task detail pages show. Stored in `project:active`. |
| Project runtime | The in-process object graph for one project: `Git`, database, `TaskService`, `ChatService`, `Scheduler`, `Reconcile`, `IntegrationService`. |
| Setup | The first-run wizard that collects the required global settings. |
| Test gate | The rule that a step's Next/Finish action stays disabled until the current values pass a live test. |
| Catalog | The models.dev `api.json` document, cached locally. |
| Adapter | The chat wire implementation for a provider: `openai` or `anthropic`. |
| Decision model | A model served by a decisions endpoint. It answers typed questions with probabilities; it does not generate text. |
| Decision API | The wire shape: OpenAI Decisions (`/decisions`) or System One (`/systemone`). |
| Unavailable project | A registered project whose path, git top level, origin, or database cannot be opened. It stays registered and does not stop the app. |

## 4. Settings model

`01-foundation` section 10 defines `KeyValueStore`. This spec adds the keys below and moves project selection out of a single `project:path`.

### 4.1 Global keys

| Key | Store | Default | Notes |
|---|---|---|---|
| `server:url` | plain | `http://127.0.0.1:5099` | Loopback only. |
| `github:app-id` | plain | — | |
| `github:installation-id` | plain | — | Default installation for projects without an override. |
| `github:webhook-url` | plain | — | Full public payload URL, for example `https://tunnel.example/api/github/webhook`. Optional. |
| `github:app-key` | secret | — | PEM private key. |
| `github:webhook-secret` | secret | — | Shared by every repository hook. |
| `github:pat` | secret | — | Classic token with `repo` scope. Required. |
| `agent:provider` | plain | — | models.dev provider id, for example `openrouter`. |
| `agent:<provider>:model` | plain | — | Selected agent model id. |
| `agent:<provider>:model:context` | plain | — | `limit.context` from the catalog at selection time, used by the harness. |
| `agent:<provider>:api` | plain | — | `openai` or `anthropic`; overrides the adapter inferred from `npm`. |
| `agent:<provider>:base-url` | plain | — | Set only when the endpoint is overridden. |
| `agent:<provider>:api-key` | secret | — | One per provider. |
| `agent:decision:provider` | plain | — | Provider id for the decision endpoint. Usually the chat provider. |
| `agent:decision:api` | plain | — | `openai` or `systemone`. |
| `agent:decision:model` | plain | — | Model id from the provider's live decision list. |
| `agent:decision:base-url` | plain | — | Set only when the decision endpoint differs from the provider base URL. |

The decision key uses the same `agent:<provider>:api-key` entry as chat. A different decision provider therefore needs its own key under its own provider id.

### 4.2 Project keys

| Key | Store | Notes |
|---|---|---|
| `projects:order` | plain | Comma-separated project ids, newest last. |
| `project:active` | plain | Active project id. |
| `project:<id>:path` | plain | Absolute, normalized git top level. |
| `project:<id>:name` | plain | Display name, at most 60 characters. |
| `project:<id>:repo` | plain | `owner/name` parsed from `origin`, lowercased. |
| `project:<id>:installation-id` | plain | Optional override when the repository is under another installation. |

### 4.3 UI keys

| Key | Store | Notes |
|---|---|---|
| `setup:step` | plain | Last completed wizard step. Removed on finish. |

### 4.4 Required setup

`SetupState.IsComplete` is derived, never stored. It is true when every required global key exists and is non-empty:

- `github:pat`
- `github:app-id`
- `github:installation-id`
- `github:app-key`
- `github:webhook-secret`
- `agent:provider`
- `agent:<provider>:api-key`
- `agent:<provider>:model`
- `agent:decision:provider`
- `agent:decision:api`
- `agent:decision:model`
- the decision provider's API key, when it differs from the chat provider

`github:webhook-url` and `server:url` are not required. A missing webhook URL is shown as a warning, not a setup failure. `project:*` is not part of `SetupState`; a complete setup with no project opens the "Add your first project" state.

Rules:

- Removing any required key makes `SetupState.IsComplete` false again, so the next start opens the wizard.
- There is no `setup:complete` flag. Re-running setup from Settings navigates to the same wizard with stored values prefilled; it does not clear anything.
- A corrupt or undecryptable secret reads as `Missing` and counts as absent, following `01-foundation`.

## 5. Wizard

### 5.1 Entry and exit

- The wizard is a full-window route (`Route.Setup`) with no navigation pane.
- It opens when `SetupState.IsComplete` is false. On first run, the board and chat are unreachable until it completes.
- It also opens from Settings → **Run setup again**; in that mode Cancel returns to Settings and the stored values are prefilled. A first run has no Cancel.
- Finishing navigates to Home when a project exists, otherwise to the empty board with "Add your first project".
- `setup:step` stores the last completed step. On entry, the wizard opens at the first step whose required keys are missing; completed steps are skipped. Back still visits a completed step, prefilled.
- A closing of the app mid-wizard keeps every value already tested and saved; tests are not persisted, so a completed step visited via Back shows `Configured` and allows Next. The final Review step re-tests everything before Finish.

### 5.2 Steps

| # | Step | Fields | Test | Required |
|---|---|---|---|---|
| 0 | Welcome | What will be collected; links to create a GitHub App and get a provider key | — | — |
| 1 | GitHub PAT | Token, write-only | `GET /user` and `GET /user/repos?per_page=1`; `repo` scope | Yes |
| 2 | GitHub App | App ID, installation ID, private key, webhook secret, webhook URL | JWT + `GET /app` + installation token + `GET /installation/repositories` | Yes |
| 3 | Provider and key | Provider, base URL, API key | `GET {base}/models` through the adapter | Yes |
| 4 | Agent model | One model from the catalog with `tool_call: true` | One short chat completion | Yes |
| 5 | Decision model | Provider, API shape, model | One predicate decision request | Yes |
| 6 | Review | Masked summary, project count | Re-runs tests 1–5 in order | — |

### 5.3 GitHub PAT

- A classic token with the `repo` scope is required. Fine-grained tokens are accepted only if the spike confirms the account-level permissions needed to create repositories and hooks; until then the wizard explains the classic `repo` requirement.
- The test calls `GET /user` and `GET /user/repos?per_page=1`. It reads `x-oauth-scopes` and requires `repo`. A pass shows the login and the accessible repository count on the next step.
- An empty or rejected token stays on the step. The previous token, if any, is not replaced.
- The token is used only by `IGitHubPersonalService`: list repositories, create a repository, and create or update a repository webhook. It never enters a git command.

### 5.4 GitHub App

- App ID is a number; installation ID is a number stored as text.
- The private key is pasted or imported from a file once. Both PEM forms are accepted: `BEGIN PRIVATE KEY` (PKCS#8) and `BEGIN RSA PRIVATE KEY` (PKCS#1). The file path is not stored.
- The webhook secret is pasted or generated (32 random bytes, base64url). Minimum 16 characters.
- The webhook URL is the full public payload URL. It must be `https` unless the host is loopback. Empty is allowed with a warning.
- The test, in order:
  1. Import the RSA key and build a JWT (`iss` = App ID, `iat` = now − 60 s, `exp` = now + 9 min).
  2. `GET /app`; show the app name and slug.
  3. `GET /app/installations`; list account logins. The user may pick one or type an installation ID.
  4. `POST /app/installations/{id}/access_tokens`.
  5. `GET /installation/repositories?per_page=1`; show the covered repository count.
- When the installation covers no repositories, the step shows the install link `https://github.com/apps/{slug}/installations/new` and a Recheck action. It does not block Next; the project add flow blocks with the same link.
- The webhook secret and URL have no synchronous remote test. Their format is validated, and the project add flow verifies the first delivery with a ping.

### 5.5 Provider and API key

- The provider list comes from the catalog. Search matches name and id.
- Each row shows name, id, `npm`, documentation link, and the resolved base URL.
- The base URL is editable. Changing it overrides `agent:<provider>:base-url`.
- **Custom endpoint** chooses a catalog provider as the metadata source, then overrides the API type (`OpenAI-compatible` or `Anthropic`), base URL, and key. The provider id and the model list stay those of the catalog provider; only the endpoint and adapter change. The override is stored as `agent:<provider>:api` and `agent:<provider>:base-url`.
- A provider absent from the catalog cannot be used for the agent role, because it has no model metadata.
- The API key is write-only and stored only after a passing test.
- The test calls the adapter's model-list endpoint and shows the model count. A 401, 404, or timeout stays on the step with the provider's error.
- When the model-list endpoint is `404`, the test falls back to a one-token chat completion with the first catalog model that has `tool_call: true`; the step labels the result as a fallback.
- Before the test sends the key, the step shows the destination host and asks for confirmation when the host differs from the provider's documentation domain.

### 5.6 Agent model

- The list is the selected provider's catalog models filtered to `tool_call === true`. Models without tool calling are not rendered at all.
- Each row shows name, id, context and output limits, input/output cost, reasoning, modalities, and release date. These values come from the catalog entry.
- The test sends one short chat completion (`Reply with the single word OK.`). Any non-empty assistant text passes. Tool support is not probed; the `tool_call` metadata is the only gate.
- A custom base URL is allowed, but the model must still be a catalog entry for the chosen provider. A provider or model absent from the catalog has no metadata, so it is not selectable for the agent role.
- If the filtered list is empty, the step says "This provider has no model with tool calling" and blocks Next.
- If `limit.context` is missing from the entry, the stored context limit defaults to 128,000.
- On pass, the wizard stores `agent:provider`, `agent:<provider>:api-key`, `agent:<provider>:model`, `agent:<provider>:model:context`, and any `agent:<provider>:api` or `agent:<provider>:base-url` override.
### 5.7 Decision model

- The provider is the chat provider and the key is reused. The step does not offer a different provider while the same provider's test can pass.
- When the test fails because the provider has no decision surface (404, or a 400 that names an unsupported endpoint), the step offers one explicit exception: **Use another decision provider**. The user picks a catalog provider that serves decisions and enters that provider's key, or enters a custom System One base URL and key. A custom endpoint is stored under the `custom-decision` provider id so its key never overwrites the chat provider's key. The exception is shown in the Review summary.
- A 401, 403, timeout, refusal, or malformed answer keeps the step on the same provider; the key or model is wrong, not the provider.
- The API shape is selected explicitly: `OpenAI Decisions` or `System One`. The default is inferred from the provider: `openai` → OpenAI Decisions; `openrouter` → System One; others → System One, with a visible warning that the shape is a guess until the test passes.
- The model list is live from the provider, never from models.dev:

| Provider | List | Notes |
|---|---|---|
| OpenAI | Fixed `gpt-6-luna` | The only model the Decisions API serves. |
| OpenRouter | `GET https://openrouter.ai/api/v1/models?output_modalities=decisions` | Public; returns decision models only. |
| System One | `GET {base}/models` | Public; entries expose `capabilities.questions` and limits. |
| Other | `GET {base}/models?kind=decision`, then `GET {base}/models` | If neither yields decision entries, allow a manual model id. |

- The test sends one predicate question about a fixed input and requires a typed answer with a probability between 0 and 1. A refusal, an empty answer, or a non-2xx response fails the step.
- On pass, the wizard stores `agent:decision:provider`, `agent:decision:api`, `agent:decision:model`, and the decision provider's API key. `agent:decision:base-url` is stored only when it differs from the provider base URL.
- The decision model is required even though no agent consumes it in v1. The probe is its only caller.

### 5.8 Review and finish

- The summary shows masked secrets, the app name, the installation account, the provider, the agent model with its context limit, the decision provider and model, the webhook URL, and the project count.
- **Test all** re-runs steps 1–5 and lists each result. Finish stays disabled until every test passes.
- Finish writes the last values, removes `setup:step`, and navigates to Home or the empty board.
- No test result or timestamp is stored.

### 5.9 Test gate

Every step implements the same rule:

- Changing any field resets the step to `Untested` and clears the previously tested value.
- A test runs for at most 30 seconds, disables the action while running, and shows the provider's error verbatim.
- Next is enabled whenever there is something to test (a non-empty field, or a value already stored), and the test runs when Next is pressed: a pass advances, a failure shows the warning and stays on the step. Finish stays disabled until every step's test passed.
- Settings edits follow the same rule: a replacement secret or model is tested before it is written. If the test fails, the old value stays.
- Tests are not persisted. A completed step visited via Back shows `Configured` and may continue; Review re-tests.

## 6. Catalog

### 6.1 Source and cache

- One source: `GET https://models.dev/api.json`. It is a single document (about 5.9 MB on 2026-11-01) containing every provider and its models.
- Cache path: `%LOCALAPPDATA%\Sebastian\catalog\models.json`. Metadata path: `models.meta.json` with `etag`, `fetchedAt`, `checkedAt`, and `bytes`.
- The catalog is fetched once per process at startup, before the wizard or Settings needs it, and on **Refresh** in Settings.
- Download limits: 15-second timeout, 16 MB cap, `User-Agent: Sebastian/<version>`, one refresh at a time.

### 6.2 Revalidation

- When a cached ETag exists, every request sends `If-None-Match` with it, verbatim, including any `W/` prefix.
- `304 Not Modified` keeps the cached document and updates `checkedAt`.
- `200 OK` replaces the cache atomically: write `models.json.tmp`, validate the root object and provider count, then move it into place. Store the response ETag as received; a weak validator is valid for GET revalidation.
- `Cache-Control` is `public, max-age=0, must-revalidate` today. There is no TTL; the ETag decides.
- If a response has no ETag, re-download at most once per 24 hours using `fetchedAt`.
- A network failure, timeout, 5xx, or invalid JSON keeps the last good cache and marks it stale with the error. It does not fail app startup.
- A `304` with no cache is treated as a failed refresh.
- A missing cache with a failed refresh blocks the wizard with a Retry action; the shell still opens existing projects.

### 6.3 Provider mapping

The `npm` property selects the chat adapter:

| `npm` | Adapter |
|---|---|
| `@ai-sdk/openai-compatible`, `@ai-sdk/openai`, `@openrouter/ai-sdk-provider`, `@ai-sdk/deepinfra`, `@aihubmix/ai-sdk-provider`, `@saladtechnologies-oss/ai-sdk-provider`, `@jerome-benoit/sap-ai-provider-v2`, `@ai-sdk/groq`, `@ai-sdk/mistral`, `@ai-sdk/xai`, `@ai-sdk/perplexity` | OpenAI-compatible |
| `@ai-sdk/anthropic` | Anthropic |
| `@ai-sdk/azure`, `@ai-sdk/google`, `@ai-sdk/google-vertex`, `@ai-sdk/amazon-bedrock`, `@ai-sdk/cohere`, `@qvac/ai-sdk-provider`, `gitlab-ai-provider` | No adapter |
| Missing or anything else | No adapter |

Rules:

- `agent:<provider>:api` overrides the adapter inferred from `npm`. The catalog provider is still the metadata source.
- A provider with no adapter is selectable only after the step shows: "No adapter is mapped for `<npm>`. Sebastian will treat this endpoint as OpenAI-compatible. Confirm the base URL before testing." The test must pass.
- The fallback warning is explicit, never silent. Azure, Google, Vertex, and Bedrock are named in the warning because their auth or URL shape usually differs.
- If the provider has no `api` field, the base URL defaults from this table when the provider id matches: `openai` → `https://api.openai.com/v1`, `anthropic` → `https://api.anthropic.com`, `groq` → `https://api.groq.com/openai/v1`, `mistral` → `https://api.mistral.ai/v1`, `xai` → `https://api.x.ai/v1`, `google` → `https://generativelanguage.googleapis.com/v1beta/openai/`. Otherwise the base URL is required.
- A base URL that is not `https` is rejected unless the host is loopback.
- A base URL containing a `${...}` placeholder requires the user to fill it; the test fails until it is replaced.
- The catalog is third-party input. Parse it with `JsonDocument`, display only the fields below, and never execute a value from it:
  - provider: `id`, `name`, `api`, `env`, `npm`, `doc`
  - model: `id`, `name`, `family`, `attachment`, `reasoning`, `tool_call`, `temperature`, `knowledge`, `release_date`, `last_updated`, `modalities`, `open_weights`, `limit.context`, `limit.output`, `cost.input`, `cost.output`, `cost.cache_read`, `canonical_model_id`

### 6.4 Adapters

- OpenAI-compatible: `OpenAIClient` with `ApiKeyCredential` and an optional endpoint, exposed as `IChatClient`.
- Anthropic: the official `Anthropic` package (12.x), `AnthropicClient.AsIChatClient(model)`. The AOT spike must construct the client and serialize one request; the package is not assumed to be AOT-safe.
- Provider test paths: OpenAI-compatible `GET {base}/models`; Anthropic `GET {base}/v1/models` with `x-api-key` and `anthropic-version`.
- The adapter is chosen per provider and shared by every project.
- `ProviderResolver` and the `appsettings.json` provider block are replaced by a factory that reads `agent:provider`, `agent:<provider>:api`, the base URL, and the key, and returns an `IChatClient`.

## 7. Decision model

### 7.1 Wire formats

OpenAI Decisions:

```http
POST {base}/decisions
Authorization: Bearer <key>

{
  "model": "gpt-6-luna",
  "input": "sebastian",
  "questions": [
    {
      "type": "predicate",
      "name": "is_word",
      "instructions": "Is the input the word sebastian?"
    }
  ]
}
```

The response has an `answers` array in question order. A predicate answer carries `probability`; a choice carries `choice`, `probabilities`, and `confidence`; a score carries `score`, `probabilities`, and `confidence`; a refusal carries a refusal type.

System One:

```http
POST {base}/systemone
Authorization: Bearer <key>

{
  "model": "<model id>",
  "state": "sebastian",
  "questions": {
    "is_word": {
      "type": "noul",
      "instructions": "Is the state the word sebastian?"
    }
  }
}
```

The response has an `answers` object keyed by question name. Each answer carries `probability`, `probabilities`, and `confidence` as the model supports them.

### 7.2 Client

- `IDecisionClient.DecideAsync(DecisionRequest, CancellationToken)` returns typed answers. No streaming, no tools, no text generation.
- Implementations: `OpenAIDecisionsClient` and `SystemOneClient`. Both use `HttpClient` and a source-generated JSON context.
- Requests are capped at 256 KB of state and 32 questions; responses at 1 MB. Timeouts are 30 seconds.
- A refusal answer is returned as a typed refusal, not thrown.
- There is no retry loop and no threshold logic in v1.

### 7.3 Listing

- OpenAI: `gpt-6-luna` is fixed and is probed directly; the chat model list is not used.
- OpenRouter: `GET https://openrouter.ai/api/v1/models?output_modalities=decisions`; fields `id`, `name`, `context_length`, `pricing.prompt`, `architecture.output_modalities`.
- System One: `GET {base}/models`; fields `id`, `displayName`, `capabilities.questions`, `limits`.
- Other providers: try `GET {base}/models?kind=decision`, then `GET {base}/models`; otherwise accept a manual id and require the probe.

### 7.4 Consumer

There is no consumer in v1. The decision model is configured, validated, and stored, and nothing calls it after setup. Chat triage, review gating, and guardrails are separate future work; when they arrive they use this client and their own thresholds.

## 8. Projects

### 8.1 Registry

- `ProjectRegistry` owns the project keys. It validates and normalizes paths, rejects duplicate paths, and keeps `projects:order` and `project:active` consistent.
- A project id is a UUIDv7. It is independent of the task ids, which live in that project's database.
- Removing a project removes its keys and closes its runtime. It never deletes the folder or the database.
- A path is normalized with `Path.GetFullPath` and compared case-insensitively.
- The registry is stored in settings, not in a database. There is no global SQLite file.

### 8.2 Add project

Three choices, all in the Projects navigation flow defined by `05`:

**Open local**

1. Folder picker.
2. The path must exist, be absolute, contain `.git`, and be the git top level.
3. `origin` must exist and be an SSH URL (`git@` or `ssh://`). HTTPS origins are rejected with the expected remote form.
4. `main` must exist locally or on `origin`.
5. The PAT must report push permission (`GET /repos/{owner}/{repo}` with `permissions.push`). The App token is not used for push; Git uses SSH.
6. The installation must cover the repository: `GET /repos/{owner}/{repo}` with the installation token. A 404 shows the install link and a Recheck action.
7. The name defaults to the repository name and is editable.
8. The project is added, made active, and its runtime is created; its database is migrated.

**Clone from GitHub**

1. Repository picker fed by `IGitHubPersonalService.ListRepositoriesAsync`, searchable and paginated.
2. Destination parent folder and folder name; the destination must not exist or be empty.
3. `git clone` over SSH with a 5-minute timeout.
4. The same checks as Open local, steps 2–8.

**Create repository**

1. Name, description, visibility, local parent folder.
2. `POST /user/repos` with `auto_init: true`.
3. Clone, then ensure the default branch is `main`: rename locally, push, and `PATCH /repos/{owner}/{repo}` `default_branch` when needed.
4. The same checks as Open local, steps 2–8.
5. The step states that the App must cover the new repository: it is available immediately under *All repositories*, otherwise it must be added under *Selected repositories*.

### 8.3 Webhook

- The payload URL is `github:webhook-url`. The secret is `github:webhook-secret`.
- After a project is added, **Create webhook** (default on when a URL and PAT exist) calls `POST /repos/{owner}/{repo}/hooks` with events `pull_request`, `pull_request_review`, and `push`, `content_type: json`, and the shared secret. An existing hook with the same URL is updated with `PATCH`.
- **Send test delivery** calls `POST /repos/{owner}/{repo}/hooks/{id}/tests` and then shows the project's webhook health. A delivery within 60 seconds marks it healthy.
- When the hook API fails or the PAT lacks hook permission, the flow shows the API error and manual instructions with the exact URL, secret, and event list.
- A missing webhook URL is a warning. Reconcile remains the safety net, as in `01-foundation`.

### 8.4 Runtime and lifecycle

- `ProjectRuntimeRegistry` creates one `ProjectRuntime` per registered project at startup, in registry order.
- A runtime owns: the project path, `RepositoryIdentity`, installation id (override or global), `Git`, `SqliteStore`, `TaskService`, `ChatService`, `Scheduler`, `ReconcileService`, and `IntegrationService`.
- The database is `<project>/.sebastian/sebastian.db`. Migrations run when the runtime is created.
- A runtime that cannot be created is marked `Unavailable` with the error. The app keeps starting and the project shows the error in the Projects list.
- The scheduler runs for every available runtime, not only the active one. A switch does not cancel runs.
- The chat session is per project, because `agent_sessions` lives in that project's database.
- Global concurrency stays at 2 across every project. A shared `RunGate` semaphore is acquired before a run starts.
- Integration uses one lock per runtime, not one per process.
- `WebApplication` content root is the application directory, not a project path. Git commands receive the project path explicitly.

### 8.5 Webhook routing

- One route, one secret, one Kestrel. `GitHubWebhookProcessor` resolves the project from `repository.full_name`.
- The processor depends on `IProjectResolver` from `Sebastian.Libs`; `ProjectRuntimeRegistry` in `Sebastian.Daemon` implements it. `Sebastian.Webhook` does not reference `Sebastian.Daemon`.
- The route map is `owner/name` lowercased to project id, built from available runtimes and rebuilt when the registry changes.
- A delivery for an unknown repository, or for a project whose runtime is unavailable, is logged and acknowledged. It is not an error.
- When `installation.id` is present, it must match the project's installation id.
- PR, review, and push handling are unchanged from `01-foundation` and `04-build-and-review`; they act on the resolved project only.
- Reconcile runs per available runtime on startup and every 5 minutes.

### 8.6 Switching and removal

- Switching sets `project:active` and navigates to Home. It does not stop runs or chat turns.
- The title bar shows the active project name and repository.
- Removal requires confirmation and is refused while the project has an active run or chat turn. It removes the keys, stops the runtime, closes the database, and leaves the folder untouched.
- A project whose path disappears behind the app becomes `Unavailable` on the next startup. Its board is replaced by the error and the Remove action.

## 9. Settings surface

`05-board` owns the layout. This spec fixes the fields and the rules:

- Global: server URL, App ID, installation ID, private key status, webhook secret status, webhook URL, PAT status, provider, base URL, agent model, decision provider, decision API, decision model, catalog refresh.
- Per project: path, name, repository, installation override, database path, webhook hook id, last delivery, last reconcile.
- **Run setup again** opens the wizard prefilled.
- **Refresh catalog** performs the conditional request and shows the ETag result.
- Replacing a secret or a model requires a passing test before the new value is written. The old value stays on failure.
- Secrets render as `Configured` / `Missing`, never their value.

## 10. Security

- The PAT is a classic `repo` token. It is used only for repository listing, repository creation, and webhook management. It never enters a git command or a log.
- The App private key is imported once, held in memory, and never logged, shown, or passed on a command line. Installation tokens live in memory.
- Every secret is DPAPI-encrypted for the current Windows user through `KeyValueStore`.
- Provider API keys are sent only to the base URL shown on the step, after explicit confirmation when the host is not the provider's documented domain.
- A non-loopback base URL must be `https`. Loopback may be `http` for local models.
- The catalog and every live model list are untrusted input. They are size-capped, parsed without reflection, displayed as data, and never used to build a command.
- The wizard has no network surface. The only inbound routes remain `/health` and the webhook route.
- Webhook deliveries are signature-verified and then matched to a registered repository and installation.
- Project paths come from the folder picker and are validated before use. The database path is always `<project>/.sebastian/sebastian.db`.
- A project boundary is a data boundary: one project's tasks, chat, runs, and worktrees are never read by another project's services.

## 11. Error and degraded states

| State | UI |
|---|---|
| Catalog offline, cache present | Use the cache; show "Catalog last checked <time>" and a warning. |
| Catalog offline, no cache | Wizard blocks with Retry; existing projects still open. |
| Catalog 304 without cache | Treat as failure; Retry. |
| PAT rejected or missing scope | Stay on the step; show the status and the required scope. |
| App key cannot be parsed | Stay on the step; name the expected PEM forms. |
| Installation does not cover the repository | Show the install link and Recheck; do not add the project. |
| Provider 401/404/timeout | Stay on the step; show the endpoint and the provider error. |
| Provider has no tool-capable model | Model step blocks; back to Provider. |
| Model test returns no text | Stay on the step; show the raw response. |
| Decision endpoint unsupported | Stay on the step; suggest another provider or API shape. |
| Decision refusal | Fail the test and show the refusal. |
| Project path missing, not git, no SSH origin, no `main` | Refuse the add; name the failed check. |
| Clone or create fails | Show the git or API error; keep the chosen folder. |
| Webhook creation fails | Show the error and manual instructions; the project is still added. |
| Runtime or migration fails | Project shows `Unavailable` with the error; other projects keep working. |
| Duplicate project path | Refuse; name the existing project. |
| Remove with an active run | Refuse; ask to close the task first. |
| Setup key removed | Wizard reopens on the next start; the board stays unreachable until it is complete. |

## 12. Testing decisions

| Seam | What is tested | Fakes |
|---|---|---|
| `SetupState` | Required keys, optional keys, removal reopens, decision key required | Temp `KeyValueStore` |
| `SetupService` | Step order, resume, stored values show `Configured`, edit resets the test, finish writes keys | Temp settings, fake tests |
| `CatalogService` | 200/304/500/timeout/corrupt/oversize/no ETag, atomic write, stale cache, one refresh at a time | Fake HTTP handler |
| Provider mapping | Every `npm` row, missing `npm`, unknown `npm` warning, default endpoints | Catalog fixtures |
| `ProjectRegistry` | Add/remove/activate, duplicate path, normalization, missing path, corrupt order | Temp settings, temp folders |
| `ProjectRuntimeRegistry` | Runtime per project, unavailable project, DB migration failure, isolation between two temp databases | Temp repos, temp SQLite |
| Add-project flow | Local checks, clone, create, branch `main` fix, install coverage, webhook create/ping | Fake PAT/App services, temp git repo, fake HTTP handler |
| Webhook routing | Known repo, unknown repo, installation mismatch, case-insensitive match, unavailable project | Octokit fixtures, fake runtimes |
| Provider test | OpenAI and Anthropic model lists, 401/404/timeout, 404 chat fallback | Fake HTTP handler |
| Agent model filter | Only `tool_call: true` is listed; empty list blocks; context limit stored | Catalog fixtures |
| `DecisionClient` | OpenAI and System One request shapes, predicate/choice/score parsing, refusal, 400/404, timeout | Fake HTTP handler |
| Decision listing | OpenAI fixed model, OpenRouter catalog, System One catalog, manual id | Fake HTTP handler |

Rules:

- No test calls models.dev, a provider, or GitHub.
- Catalog fixtures are small JSON documents with the same shape as the real entries.
- Live tests use a fake `HttpMessageHandler`; the decision probe is asserted against captured requests.
- Project tests use temp folders and local bare remotes, following the existing `TestRepository` pattern.
- The settings test gate is tested at the service level; the UI is checked manually.
- The AOT publish spike adds one Anthropic client construction, one catalog `JsonDocument` parse, and one decision request/response serialization.

Acceptance criteria:

1. A fresh profile opens the wizard; the board is unreachable; finishing opens Home or "Add your first project"; the wizard does not return while every required key exists.
2. Killing the app mid-wizard resumes at the last completed step, shows stored values as `Configured`, and never renders a secret.
3. An invalid PAT stays on the step and does not replace the stored token; a valid token shows the login and repository count.
4. An invalid App key or installation stays on the step; a valid one shows the app name, installation account, and repository count.
5. A provider with a mapped `npm` gets the matching adapter; an unknown `npm` shows the OpenAI fallback warning and cannot continue without a passing test; a custom endpoint can override the adapter and base URL but still lists only catalog models with `tool_call: true`.
6. The agent model list contains only `tool_call: true` models; a provider without one blocks the step; no chat tool probe is used.
7. The decision step lists models from the provider, not models.dev; a rejected probe stays on the step; a passing probe stores the keys.
8. Removing `github:pat` or `agent:decision:model` reopens the wizard on the next start.
9. Two projects run in one process: each has its own database, chat thread, board, and worktrees; a task created in one is absent from the other; the global run cap stays 2.
10. A webhook for project A updates only project A; an unknown repository is ignored; a mismatched installation is ignored.
11. Adding the same path twice is rejected; removing a project leaves its folder and database intact.
12. The catalog uses the stored ETag: a `304` keeps the cache with no re-download, a `200` replaces it atomically; an offline start with a cache still works.
13. A project with a missing path is `Unavailable` and does not stop the app or other projects.
14. Replacing a model or secret in Settings is refused when the test fails, and the old value still works.

## 13. Out of scope

- Multi-user, signup, roles, multi-PC sync, or a remote database.
- Cross-project tasks, dependencies, chat, or runs.
- Custom agent models that are absent from models.dev; they have no tool metadata and are not selectable.
- Providers that are absent from models.dev; a custom endpoint must name a catalog provider for model metadata.
- Native Azure, Vertex, Bedrock, Google, or Cohere SDK adapters; they use the OpenAI-compatible fallback only.
- The GitHub App manifest creation flow; the wizard links to GitHub and asks for the values.
- An automatic tunnel; the user supplies the public webhook URL.
- Decision-model consumers, thresholds, retries, or guardrails.
- Auto-updating the catalog beyond startup and the manual Refresh action.
- Renaming an existing repository's default branch to `main`.
- Deleting project folders or databases.
- More than one GitHub App per process; multiple installations of that App are supported.

## 14. Risks and open items

- **One runtime per project** means N SQLite connections, N schedulers, and N reconcile timers in one process. A handful of projects is fine; v1 does not bound the number.
- **Webhook routing by `owner/name`** assumes one repository per project. Two projects cloned from the same repository cannot both receive deliveries.
- **Per-account installations** are supported through an optional project override. The wizard captures one default; a project on another account must supply another installation id.
- **models.dev availability** is now on the critical path for setup and Settings. The cache covers restarts, not a first run with no network.
- **The Anthropic adapter** is unverified for AOT and for tool-calling parity; the spike decides it before implementation continues.
- **The catalog is third-party** and can change shape. A parse failure is recoverable through the cache, but a schema change needs a code update.
- **Tool support is metadata-only.** A catalog entry that says `tool_call: true` can still be wrong; the first real run is the first proof.
- **Decision model has no consumer** in v1. It is required by product decision, so a broken decision endpoint blocks setup even though no task uses it.
- **Anthropic has no decisions endpoint.** A chat provider without a decision surface needs the explicit decision-provider exception in 5.7, or setup cannot complete.
- **Project removal does not delete data.** A stale `.sebastian` folder remains the user's responsibility.
- The fine-grained PAT path is not verified; classic `repo` is the documented token.

## 15. References

| Topic | URL |
|---|---|
| models.dev catalog | <https://models.dev/api.json> |
| OpenAI Decisions API | <https://developers.openai.com/api/docs/guides/decisions> |
| OpenAI Decisions reference | <https://developers.openai.com/api/reference/resources/decisions/methods/create> |
| Azure OpenAI Decisions | <https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/decisions> |
| System One API | <https://docs.system-one.dev/en/docs/api> |
| OpenRouter decision models | <https://openrouter.ai/docs/guides/community/jev> |
| OpenRouter decision catalog | <https://openrouter.ai/api/v1/models?output_modalities=decisions> |
| Anthropic C# SDK | <https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp> |
| Microsoft Agent Framework providers | <https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/> |
