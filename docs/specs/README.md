# Sebastian v1 Specifications

Specs for the chat-first work board described in [`docs/intent.md`](../intent.md). Written in English; the source intent is Indonesian. Code identifiers and the domain terms below are canonical.

Status: draft for review. Nothing here is implemented yet.

These are product specs kept in the repository. Specs generated for `type=spec` tasks at runtime live in SQLite (`tasks.body`) and are never committed to the repository.

## Documents

| Doc | Covers | Read after |
|---|---|---|
| [01-foundation](01-foundation.md) | Runtime, task model, state machine, SQLite schema, scheduler, webhook adapter, config, security, test seams | — |
| [02-setup](02-setup.md) | First-run wizard, test gate, models.dev catalog, provider adapters, decision model, project registry and runtimes | 01 |
| [03-chat-and-spec](03-chat-and-spec.md) | Chat streaming, chat tools, task creation, spec generation, spec review gate | 01, 02 |
| [04-build-and-review](04-build-and-review.md) | Build agent, verification, integration, conflicts, one PR per parent, review agent, merge | 01–03 |
| [05-board](05-board.md) | Navigation, projects, kanban, task detail, actions, settings, accessibility | 01–04 |

`01-foundation` is the contract. The other four reference it instead of redefining the model.

## Decision log

These decisions refine or replace statements in the intent. They are intentional, not drift.

| # | Decision | Why |
|---|---|---|
| 1 | The domain term is **task**, not ticket. A task can have one level of sub-tasks. | Correction from the product owner; sub-tasks are execution units. |
| 2 | The app is a single WinUI 3 / Reactor desktop process. The ASP.NET Core host runs in-process only for GitHub webhooks. There is no board HTTP API. | One process, no localhost UI surface, reuse the existing shell. |
| 3 | A **parent** integrates; a **leaf** executes. Only leaves are built by agents. | Parallel sub-tasks need explicit execution units and one review artifact. |
| 4 | One PR per parent. Leaves get their own branch/worktree, but not their own PR. | One review and one merge for the human; parallel work stays isolated. |
| 5 | A dependent leaf waits until its blocker is **integrated into the parent branch**, not until the PR merges. | The PR does not exist until the end; integration is the code-availability point. |
| 6 | Dependencies are sibling-only; hierarchy is one level. | Keeps the scheduler and board understandable in v1. |
| 7 | The chat agent creates the task tree and dependencies directly. The Spec agent only writes the spec body. | Product-owner choice; chat is the only intake. |
| 8 | A spec is frozen after approval. A scope change is a new task. | Prevents orphaned children and silent spec drift. |
| 9 | SQLite via `Microsoft.Data.Sqlite`, `Dapper.AOT` for queries, `DbUp` for migrations; no EF Core. | AOT-friendly and small enough for the data model. |
| 10 | AOT is the publish target. A publish spike runs before implementation; trimmed is the documented fallback. | Reactor and the current host already support AOT; verify the combined app. |
| 11 | GitHub App auth and `Octokit.Webhooks` stay. The processor handles PR/push events; issue intake is removed. | Product-owner choice; the existing webhook adapter is reusable. |
| 12 | Git push uses the existing SSH remote; the App token is used only for PR/merge/comment API calls. | No token in git commands or logs. |
| 13 | The sub-task visual layout follows the existing prototype; the data and actions are fixed in `05`. | Prototype already exists; layout is deferred. |
| 14 | The host owns verification, commits, integration, push, PR creation, and merge. The build agent only edits files. | Keeps the agent sandbox confined and makes the merge/approval rules enforceable. |
| 15 | A fifth status, `closed`, marks a task cancelled by a human or a PR closed without merge. The board hides `closed` tasks unless the `Show closed` toggle is on. | Keeps `done` meaning delivered, and keeps the four columns from the intent unchanged. |
| 16 | Microsoft Agent Framework owns conversation state and history: the serialized `AgentSession` is the single store in `agent_sessions`, and there is no chat message table. | Persistence is handled by the framework; one blob avoids two sources of truth for the same transcript. See `01-foundation` section 15 for the framework docs. |
| 17 | No `runs`, `verifications`, `spec_versions`, `task_links`, `chat_tool_calls`, or `pull_requests` tables. PR state is read from the origin repository, run state lives in memory, verification output goes to the log, and spec versions are not kept. | The database holds only state that cannot be reconstructed. PR state already lives in GitHub; duplicating it only creates staleness. |
| 18 | No configuration file. Non-secret settings live in Windows `LocalSettings` behind one `KeyValueStore`, secrets are DPAPI-encrypted per Windows user, projects are added and switched from the Projects navigation, and there is no token or wall-clock budget. | Settings are edited in-app; a JSON file plus environment variables was two sources of truth. Plaintext secrets never touch disk. |
| 19 | Two GitHub services with separate credentials: `IGitHubAppService` (installation token) for PR, review, and comments; `IGitHubPersonalService` (PAT) only for listing and creating repositories and managing their webhooks. | The App cannot act as the user, so personal repository creation and hook setup need a user token. Separate types stop the broad token from leaking into PR code paths. |
| 20 | One process serves several registered projects. Each project has its own database, chat session, tasks, scheduler, worktrees, and webhook target. The registry lives in settings, not a database. The run cap stays 2 across every project. | The navigation already models projects; one user works in more than one repository. Per-project state keeps tasks and branches isolated. |
| 21 | Setup requires the PAT and the decision model. Every credential and model is tested live before it is written; a failed test keeps the old value. The agent model must have `tool_call: true` in models.dev, and models without it are not listed. | A half-configuration produces runs that fail later. Metadata is the source of truth for tool support; a probe is not. |
| 22 | models.dev is the provider and model catalog. It is revalidated with its ETag on every start (verified `304`), cached on disk, and has no TTL. The decision model list comes from the provider, not models.dev. | The catalog changes on its own schedule; `Cache-Control: max-age=0` and a working ETag make a fixed TTL pointless. |
| 23 | A decision model is a typed decision API (OpenAI Decisions or System One), not a general LLM. It is configured and probed in v1 but has no agent consumer yet. | The API returns calibrated probabilities and cannot write; wiring it into chat or review is separate work. |
| 24 | The wizard is global; project selection lives in the Projects navigation. Setup completion is derived from the required keys, with no setup flag. | One flag would drift from the keys. Projects are added and switched where the shell already puts them. |
| 25 | Provider adapters are chosen by the models.dev `npm` property. Unmapped `npm` values show an explicit OpenAI-compatible fallback warning and require a passing test. Custom base URLs are allowed only on a catalog provider and model. | The `npm` field is the only provider-shaped metadata available; silent fallbacks hide incompatible auth or endpoints. |

## Resolved open questions from the intent

| Intent question | Resolution |
|---|---|
| Spec revision | Regenerate through chat before approval. Frozen after approval; new task for a scope change. |
| Type promotion | Not supported in v1. |
| Spec for other types | No spec gate on non-spec tasks. A complex change gets a separate `spec` task. |
| Spec generation order | The chat creates the task tree first; the Spec agent then writes the body. |
| Chat session | One continuous thread per project. Multiple sessions per project are out of scope. |
| PRD | `PRD 0.1` is superseded by these specs; a PRD 0.2 rewrite follows separately. |

## Open items

- AOT spike result for `Sebastian.Windows` with WinUI, Reactor, Kestrel, Octokit, and Anthropic in one binary.
- Whether the Anthropic adapter and the decision client survive the AOT spike without trimming changes.
- The first decision-model consumer and its thresholds; v1 only configures and probes it.
- Fine-grained PAT support versus the documented classic `repo` token.
- Final sub-task layout from the existing prototype.
- Whether the build agent's commit identity stays the repository user or becomes a bot identity.
- models.dev schema drift and the recovery path when a future shape cannot be parsed.

## Known deviations and risks

- The intent says "GitHub App untuk PR dan merge"; that holds. Git push is still SSH, so commits are authored as the local user, not the App.
- The intent's success metric "human minutes per ticket" cannot be measured by the system, and no transition history is stored, so cycle time is not instrumented either.
- Run and verification history is not persisted. A restart keeps tasks, branches, and worktrees, but the failure detail is only in the log.
- There is no token or wall-clock budget, so an agent run that hangs without a shell command has no timeout.
- DPAPI secrets are bound to the Windows user profile. Moving the app to another PC or user requires re-importing the private key, webhook secret, and API keys.
- The PAT grants access as the user. It is confined to `IGitHubPersonalService`, but a bug that hands it to another call path would widen the blast radius beyond the App's four permissions.
- Parallel integration can conflict. v1 attempts one automatic resolve, then blocks; it does not auto-resolve indefinitely.
- Task trees are immutable in v1. If the Spec agent's output calls for a different decomposition, the human closes the task and asks chat for a new tree; there is no edit tool.
- Kestrel inside an MSIX app needs a firewall rule, and the app must be running for webhook delivery. Startup reconcile and a periodic reconcile cover missed events.
- The decision model is required by setup but has no consumer in v1, so a broken decision endpoint blocks setup without affecting any run.
- A custom agent model that is absent from models.dev has no `tool_call` metadata and cannot be selected for the agent role.
- One GitHub App is supported per process. Projects on different accounts need a per-project installation override; a second App is out of scope.
- One repository maps to one project. Two projects cloned from the same repository cannot both receive webhooks.
- models.dev is on the setup path. A cache covers restarts, not a first run with no network, and the catalog is third-party data.
- `skills/to-spec/SKILL.md` still publishes to GitHub Issues with a `feature` label. It conflicts with this direction and must be updated separately.
- `docs/intent.md` still uses "ticket"; these specs use "task". The intent should be updated separately so the vocabulary does not drift.
