# Specification 01 — Foundation

Status: draft for review
Date: 2026-11-01
Source: `docs/intent.md`
Agent framework: [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) (`Microsoft.Agents.AI.*`), see section 15
Related: [02-setup](02-setup.md), [03-chat-and-spec](03-chat-and-spec.md), [04-build-and-review](04-build-and-review.md), [05-board](05-board.md)

## 1. Problem

Sebastian today is a web service whose only intake is a GitHub Issue and whose only agent output is an uncommitted worktree. There is no database, no chat, no board, no spec gate, and no delivery loop. The product direction in `docs/intent.md` replaces that with a chat-first work board running as one self-hosted desktop process.

This spec defines the shared foundation for that product: the runtime shape, the task model, the state machine, persistence, scheduling, the GitHub webhook adapter, configuration, and the test seams. It is the contract the other four specs build on.

## 2. Scope

In scope:

- One WinUI 3 Reactor desktop process that also hosts a small ASP.NET Core endpoint for GitHub webhooks.
- A project registry with one project runtime per registered project, so one process serves several repositories.
- A task model with one level of sub-tasks and sibling dependencies.
- The five-status lifecycle and the spec gate.
- SQLite persistence for tasks, dependencies, and the agent session. PR state, run state, and verification results are not stored; they come from the origin repository, the live process, and the logs.
- The scheduler that turns ready leaves into agent runs.
- Agents built on Microsoft Agent Framework (`HarnessAgent`), with conversation state owned by the framework and mapped to SQLite.
- The Octokit-based webhook adapter and PR reconciliation, routed by repository to a project.
- Global setup, provider and model selection, the models.dev catalog, and the decision-model configuration (defined in `02`).
- Settings, secrets, logging, security boundaries, and test strategy.

Out of scope here: setup, projects, and models (`02`), the chat experience (`03`), build/integration/review behaviour (`04`), and board interaction (`05`).

## 3. Terms

| Term | Meaning |
|---|---|
| Task | A unit of work. Stored in `tasks`. The board item. |
| Type | One of `spec`, `bug`, `refactor`, `chore`, `enhance`, `security`. Fixed. |
| Status | One of `not_started`, `in_progress`, `in_review`, `done`, `closed`. Fixed. |
| Leaf | A task with no children. Only leaves execute. |
| Parent / container | A task with at least one child. It integrates work; it is not built by an agent. |
| Integration task | The task that owns the integration branch and the PR: a parent container or a top-level leaf. |
| Sub-task | A child task. One level only in v1. |
| Dependency | A task that must reach a threshold before another may start: a top-level task that must be `done`, or a sibling sub-task that must be `in_review` or `done`. |
| Integration branch | The integration task's local branch `sebastian/task-<id>`. All sub-task work is merged here. |
| Leaf branch | `sebastian/task-<parentId>-<leafId>`. The agent's worktree branch. |
| Run | One agent execution: `spec`, `build`, `review`, or `resolve`. |
| Verification | A host-run command (`build`, `test`, `lint`) with exit code and captured output. |
| Integrated | A leaf whose verified branch is merged into the integration task's branch (or, for a top-level leaf, whose PR is open). |
| Blocked | A derived badge meaning the last run, verification, integration, or PR delivery failed. Not a status. |
| Spec Review | A derived badge on a `type=spec` task that is waiting for human approval. |
| Project | A local git top-level folder with its own database, chat session, tasks, scheduler, and PR flow. Identified by a UUIDv7. |
| Active project | The project shown on Home, Board, and Task detail. Stored in `project:active`. |
| Project runtime | The in-process services for one project: Git, database, task store, chat, scheduler, reconcile, integration. |
| Global settings | Settings shared by every project: GitHub App, PAT, providers, models, decision model. |
| Setup | The first-run wizard that collects the required global settings. Defined in `02`. |
| Catalog | The cached models.dev `api.json` document, used for provider and model metadata. |
| Decision model | A model served by a decisions endpoint that answers typed questions with probabilities. It does not generate text. |

## 4. Runtime topology

One process, one user, one or more projects. `Sebastian.Windows` is the only executable. The code is split into five .NET projects — one executable and four class libraries — that all load into that process. The split is compile-time only: no IPC, no localhost service boundary between them, and no separate lifecycle. The existing root `Sebastian.csproj` and `Program.cs` are deleted; their contents move into the libraries below.

Rules:

- The board and chat call services directly through the app's DI container. There is no localhost UI API and no SSE channel.
- The only HTTP surface is `/health` and the GitHub webhook route registered by `MapGitHubWebhooks(secret: ...)`, both provided by `Sebastian.Webhook`. Both bind to a loopback URL; a tunnel or reverse proxy may forward to it.
- The `WebApplication` is built with `ContentRootPath` set to the application directory. Git commands receive the selected project path explicitly; opening or cloning a project validates that it is the git top level.
- Settings and secrets live in `ApplicationData.Current.LocalSettings` and a DPAPI store; there is no configuration file (section 10).
- The host starts before the main window is shown. With required global settings missing, the app shows the setup wizard (`02`). With setup complete and no project registered, the app shows "Add your first project". If Kestrel fails, the app shows a fatal error state. A project whose GitHub authentication fails is marked `Unavailable` and does not stop the others.
- Missing webhook deliveries are not fatal. The app reconciles PR state on startup and on a timer (section 9.5).
- One `ProjectRuntimeRegistry` creates a runtime per registered project at startup. A project with a missing path, broken database, or changed origin is marked `Unavailable`; the app and the other projects keep running.
- The existing hosted services (`Git`, `JobQueue`) become in-process services with the same lifecycle. The scheduler replaces the issue-keyed queue admission.
- Each library registers itself through an `AddSebastian<Area>(IServiceCollection)` extension. `Sebastian.Windows` is the only composition root.

### 4.1 Solution layout

All projects sit at the repository root. There is no `src/` and no shared `tests/` umbrella; each library has its own sibling test project.

| Project | Kind | Contents | Depends on |
|---|---|---|---|
| `Sebastian.Libs` | class library | `Domain`, `Persistence`, `Configuration`, `Settings`, `Projects`, `Catalog`, `Git`, `GitHub/App`, `GitHub/Personal`, `Logging` | — |
| `Sebastian.Agent` | class library | `Roles`, `HarnessAgent` setup, session store, prompts, tool schemas, `IChatClient` wiring, `Models`, `Decisions` | `Libs` |
| `Sebastian.Daemon` | class library | `Projects`, `Scheduler`, `Jobs`, sandbox/runner, `Verification`, `Integration`, `Reconcile`, `Chat` | `Libs`, `Agent` |
| `Sebastian.Webhook` | class library | `MapGitHubWebhooks`, `GitHubWebhookProcessor`, `/health` | `Libs` |
| `Sebastian.Windows` | WinExe (WinUI 3 Reactor) | bootstrap, `IHost`, shell and pages | `Daemon`, `Webhook` |
| `Sebastian.Libs.Tests` | xUnit | task model, persistence, settings, project registry, catalog, git, GitHub App and personal services | `Libs` |
| `Sebastian.Agent.Tests` | xUnit | roles, prompts, tool calls, provider adapters, decision client, fake `IChatClient` | `Agent` |
| `Sebastian.Daemon.Tests` | xUnit | project runtime, scheduler, runs, sandbox, verification, integration, reconcile, chat service | `Daemon` |
| `Sebastian.Webhook.Tests` | xUnit | webhook transitions, duplicate delivery, repository routing, push sync | `Webhook` |

Rules:

- `Sebastian.Daemon` and `Sebastian.Webhook` are **class libraries**, not processes. The names describe responsibility only; both are hosted by `Sebastian.Windows`.
- Area folders live inside the owning project (for example `Sebastian.Daemon/Scheduler/`, `Sebastian.Libs/Domain/`). Adding an area does not create a new project.
- Shared analyzer settings (`TreatWarningsAsErrors`, `AnalysisLevel latest-all`, `EnforceCodeStyleInBuild`) live in a root `Directory.Build.props`. `IsAotCompatible=true` is set on the four libraries. `PublishAot`, `InvariantGlobalization`, and the explicit `RuntimeIdentifier` stay on `Sebastian.Windows` only (section 4.2).
- `Sebastian.Webhook` carries `FrameworkReference Microsoft.AspNetCore.App`; `Sebastian.Windows` carries it too because it builds and runs the `WebApplication`.
- A test project references its library directly and other libraries transitively; only the library under test gets `InternalsVisibleTo`. Every test seam in section 12 has one owning project.
- The app's window bootstrap builds the host, creates the project runtimes, starts the host, then renders the shell. The existing `Bootstrap/App.cs` navigation stays; the `Projects` items list the registered projects and add, switch, or remove them.

### 4.2 AOT

AOT is the target, not a hope. Reactor documents Native AOT support, and the current host already publishes AOT with Octokit.Webhooks.

Required properties on `Sebastian.Windows` in Release:

```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

A publish spike is the first implementation task. It must run, not just build: one DbUp upgrade, one Dapper.AOT query, one `AgentSession` serialize/deserialize round-trip, one Anthropic `IChatClient` construction, one catalog `JsonDocument` parse, and one decision request/response serialization execute in the published binary, because the libraries, the Anthropic SDK, and the framework's JSON metadata can otherwise hide a runtime failure. If the spike produces a diagnostic that cannot be fixed without dropping a needed feature, the documented fallback is `PublishTrimmed=true`; the rest of this spec does not change. AOT-hostile Reactor features (`AutoColumns<T>`, PropertyGrid auto-discovery, `UseObservable` on POCOs, devtools) are not used in Release.

## 5. Task model

### 5.1 Fields

`tasks` stores only the minimal fields plus internal timestamps:

| Field | Type | Notes |
|---|---|---|
| `id` | text | UUIDv7, generated by the app. Branch names use it. |
| `parent_id` | text | Nullable. `tasks.id` of the parent; one level only. |
| `type` | text | One of the six fixed types. |
| `status` | text | One of the five fixed statuses. |
| `title` | text | Short label. |
| `body` | text | Description, or the latest generated spec for `type=spec`. |
| `created_at` | text | ISO-8601 UTC. |
| `updated_at` | text | ISO-8601 UTC. |

Everything else is a relation or lives outside the database: dependencies are rows, while run state, PR state, verification output, and spec history are not stored at all. Tasks are scoped to one project database; a task never references a task in another project.

### 5.2 Tree shape

- Exactly one level of sub-tasks in v1. A child cannot have children; the service rejects it.
- A child has at most one parent. A top-level task has no parent.
- Dependencies connect either two top-level tasks or two siblings under the same parent. A mixed scope is rejected by the service and by a database trigger.
- A sub-task dependency never crosses a parent. A requirement that spans parents is expressed as a task-level dependency on the parents instead.
- The service rejects self-dependencies and cycles within a scope.
- A parent with children is a container. A parent with no children is a leaf.
- A `type=spec` task may be a leaf (its own build) or a container (the spec describes the children's work).
- The chat agent creates the tree; the Spec agent only writes the spec body. The tree is immutable after creation; a different decomposition is a new task.

### 5.3 Body ownership

- For a non-spec task, `body` is the human-readable description created from chat.
- For a spec task, `body` is the latest generated spec. The original chat request remains in the chat session state; the `taskId → MessageId` map in `AgentSession.StateBag` links the task to the `ChatMessage.MessageId` that created it.
- Each generated spec replaces `tasks.body`. There is no version history; regeneration before approval overwrites.
- After a spec task is approved, its body is frozen. Regeneration and direct editing are rejected. A scope change is a new task.

## 6. State machine

### 6.1 Statuses

`not_started` → `in_progress` → `in_review` → `done` or `closed`.

`done` and `closed` are terminal. Reopening is out of scope. A blocked task keeps its status and gains a badge. `closed` means a human cancelled the task, or its PR was closed without merge; `done` means the work was delivered.

### 6.2 Transitions

| # | From | To | Trigger | Guard | Side effects |
|---|---|---|---|---|---|
| 1 | — | `not_started` | Chat creates a task | Type valid; tree valid | Insert task, children, dependencies; link chat message. For `type=spec`, enqueue a `spec` run. |
| 2 | `not_started` | `not_started` | Spec run succeeds | Latest run is `spec` | Write `body`; badge becomes `Spec Review`. No status transition. |
| 3 | `not_started` | `in_progress` | Human approves a spec task | Type is `spec`; latest spec run succeeded; all task-level blockers `done`; for a child, its parent is `in_progress` | Freeze body; create integration branch if container; enqueue ready leaves or the leaf build. |
| 4 | `not_started` | `in_progress` | Human starts a non-spec task | No spec gate; all task-level blockers `done`; for a container, children exist; mirror `main` is clean | Create integration branch if container; enqueue ready leaves or the leaf build. |
| 4a | `not_started` | `in_progress` | Scheduler starts a ready child leaf | Readiness rules in 7.1; parent is `in_progress` | Create worktree/branch if absent; enqueue build. |
| 5 | `in_progress` | `in_review` | Host opens the single PR | All required leaves integrated or closed; integration verification passed; PR created | Parent enters review. |
| 5a | `in_progress` | `in_review` | Leaf integrated into the parent branch | Leaf and integration verification passed | Unblock dependent leaves. |
| 6 | `in_review` | `done` | PR merged (webhook or reconcile) | PR state merged | Mark parent and all leaves `done`. |
| 7 | `in_review` | `closed` | PR closed without merge (webhook or reconcile) | PR state closed | Mark parent and all leaves `closed`. |
| 8 | any non-terminal | `closed` | Human closes the task | Confirmation | Cancel active runs; close open PR via the App; close children that are not `done` or `closed`. |
| 9 | `in_progress` | `closed` | Human closes a leaf | Confirmation; warn when a dependent still needs it | Exclude the leaf from integration; dependents stay blocked. |

A leaf's own lifecycle is the same five statuses:

- `not_started`: not yet picked up.
- `in_progress`: a build run is queued, running, or retrying, or the latest attempt failed.
- `in_review`: verified and integrated into the parent branch (or, for a top-level leaf, the PR is open).
- `done`: the parent PR merged.
- `closed`: the parent PR was closed without merge, or the leaf was cancelled.

A parent's status is its own lifecycle, not a computed average of its children. The parent stays `in_progress` while leaves run, moves to `in_review` only when its PR exists, and becomes terminal only on merge or close. Child progress is shown as a badge and in the detail, not by moving the parent early.

### 6.3 Spec gate

- A `type=spec` task cannot start before a human approves its spec. This applies to a leaf spec task and to a spec container's children.
- The Spec agent writes the body while the task remains `not_started`. The UI shows `Generating spec` while the run is active and `Spec Review` when it succeeds.
- Approval is a human-only UI action. The chat agent has no approve tool.
- A failed spec run leaves the task `not_started` with a `Blocked` badge; retry is a human action.
- Approval is the transition itself; there is no separate approval row.

### 6.4 Blocked

`Blocked` is derived from the current process and the last attempt, never stored as a status or a column:

- The latest run for the task failed or timed out.
- The latest leaf or integration verification failed.
- The latest integration attempt conflicted.
- The latest PR push, creation, or merge attempt failed.
- The task is `in_progress` but the process has no active run for it, which covers a restart mid-run and reads as `stalled`.
- Any child of a container that is neither `done` nor `closed` is blocked (the parent shows the badge too).

The badge clears when a subsequent run and verification succeed, or when the task is closed. Because nothing is persisted, the failure text is only available until the process restarts; the log keeps the detail.

A task waiting on a dependency is **not** blocked, at either scope. It is `not_started` with a "waiting on #x" hint in the detail.

### 6.5 Close semantics

- Closing a parent cancels its active runs, closes its open PR through the App, and closes all children that are not `done` or `closed`.
- Closing a leaf excludes it from integration. Dependents that need it stay blocked. The parent may still finish with the remaining integrated leaves.
- A parent with no integrated leaves and all children closed can only be closed; it never opens a PR.

### 6.6 Restart

- There are no run rows to mark. A leaf left `in_progress` with no live run after startup shows `stalled`.
- Chat turns live inside the serialized `AgentSession`. A turn interrupted by a crash keeps its partial text from `AgentSession.StateBag` and is rendered as `interrupted` on the next start; completed messages survive in the restored session.
- Tasks, worktrees, branches, and integration branches survive. PR links are recovered from the origin repository by head branch.
- The scheduler does not auto-resume interrupted work. Retry is a human action.
- PR state is reconciled on startup (section 9.5).

## 7. Scheduling and concurrency

### 7.1 Ready leaf

A leaf is ready when all of these hold:

1. Its status is `not_started`.
2. It has no queued or running run in the live registry.
3. Its parent, if any, is `in_progress`.
4. If its parent is `type=spec`, that parent's spec is approved.
5. Every `task_dependencies` blocker has reached its scope's threshold: a top-level blocker is `done` (its code is on `main` only after the merge), and a sibling blocker is `in_review` or `done` (integrated into the parent branch). A `closed` blocker never satisfies a dependency.

The scheduler evaluates ready leaves on task transitions, on leaf integration, and on startup. It may also poll on a short interval. Each project runtime evaluates its own leaves; the source of truth is that project's database, not an in-memory set.

### 7.2 Concurrency

- Concurrency is fixed at 2. It is a global cap across spec, build, review, and resolve runs in the current process, shared by every project. There is no separate per-parent or per-project cap; a parent with more ready leaves than the cap runs them in batches.
- One active run per leaf. A duplicate enqueue is rejected by key `run:<taskId>:<kind>` in the live registry.
- Integration is a host operation, serialized by one lock per project runtime. Two leaves may finish at the same time; only one merges into that project's integration branch at a time.
- There is no token or wall-clock budget. The only time limit is the sandbox's per-command timeout.

### 7.3 Worktrees and branches

- Every worktree lives under the project's `<repo>/.sebastian/workspaces/`. No worktree is created anywhere else, and the folder is gitignored.
- Parent integration worktree: `.sebastian/workspaces/task-<parentId>-integration` on branch `sebastian/task-<parentId>`, created from the current clean `main` when the parent starts. Merges, integration verification, and the PR push all happen in this worktree.
- Leaf worktree: `.sebastian/workspaces/task-<parentId>-<leafId>`.
- Leaf branch: `sebastian/task-<parentId>-<leafId>`, created from the integration branch HEAD at the time the leaf starts.
- A top-level leaf uses `.sebastian/workspaces/task-<id>` and branch `sebastian/task-<id>`; its branch is also its integration branch.
- Branch and worktree names use the full UUIDv7 id.
  - shortcut: the leaf worktree path is up to ~102 characters relative to the repo root, which can approach MAX_PATH for deeply nested repositories; shorten to an id prefix only if that happens.
- Worktrees are reused on retry so uncommitted or partial agent work survives. The integration branch is merged into the leaf worktree before a retry when it has advanced.
- Worktrees are not deleted automatically after `done`; they are the audit trail. Cleanup is a later feature.

### 7.4 Retry

- Retry is a human action on a blocked leaf.
- Before the new run, the host merges the current integration branch into the leaf worktree. If that merge conflicts, the host leaves the conflict markers and runs one `resolve` run. If resolution fails or conflicts again, the leaf stays blocked; no further automatic attempts.
- After verification succeeds, the leaf is integrated again.

## 8. Persistence

### 8.1 SQLite

- `Microsoft.Data.Sqlite` 10.x with the bundled `e_sqlite3` native library (pinned by the package, not by the OS) for connections, plus `Dapper.AOT` for queries. There is no runtime ORM and no reflection-based mapping.
- Every SQL statement is a literal `const` at the call site so the Dapper.AOT source generator can intercept it. A call site that cannot be generated is a build error because `TreatWarningsAsErrors` is on.
- Migrations use `dbup-sqlite` 6.x with embedded `.sql` scripts ordered by name (`0001_init.sql`, `0002_...`). The entry point is `DeployChanges.To.SqliteDatabase(...)` with `WithScriptsEmbeddedInAssembly(...)`.
  - `To.SqlDatabase` and `WithScriptsAndCodeEmbeddedInAssembly` are not used; both depend on reflection that the AOT analyzer cannot see.
  - `dbup-sqlite` references `Microsoft.Data.Sqlite.Core` and ships no native binary. The app references `Microsoft.Data.Sqlite` itself, otherwise startup fails with `You need to call SQLitePCL.raw.SetProvider()`.
- Schema version is DbUp's `schemaversions` journal. Each script and its journal row run in one transaction. The database file is copied to `sebastian.db.bak` before an upgrade that has pending scripts.
- `dbup-core` targets netstandard2.0 without AOT annotations, so publish reports IL2xxx warnings from inside DbUp. Those codes are suppressed narrowly in `Sebastian.Libs`.
  - shortcut: a bad suppression fails at runtime after publish, not at build; the AOT spike runs a real upgrade and every DbUp upgrade re-checks the suppression.
- Database file: `<repo>/.sebastian/sebastian.db`, one per project. This folder is already gitignored. There is no global database; the project registry lives in settings.
- `PRAGMA journal_mode=WAL`, `PRAGMA foreign_keys=ON`, `PRAGMA busy_timeout=5000`.
- Writes are serialized through one `SqliteStore` gate. Reads may run concurrently under WAL.
- Timestamps are ISO-8601 UTC text. Primary keys are UUIDv7 text generated in the app with `Guid.CreateVersion7()`.
- `type` and `status` are C# enums (`TaskType`, `TaskStatus`) stored as text, mapped through a declarative Dapper.AOT type handler. The database `CHECK` constraints stay as the second gate.
- Conversation state and chat history are owned by Microsoft Agent Framework and stored entirely in `agent_sessions.state`: the serialized `AgentSession`, using the harness's default in-session history provider. There is no chat message table; the host reads messages from the restored session. Each project database has its own chat session row; sessions never cross projects.
- There is no `runs`, `verifications`, `spec_versions`, `task_links`, `chat_tool_calls`, or `pull_requests` table. Run state is a live in-memory registry, verification output goes to the log, PR state is read from the origin repository, spec versions are not kept, and the task-to-message link lives in `AgentSession.StateBag`.
- The database is local disk only. Do not place it in a cloud-synced folder.

### 8.2 Schema

```sql
CREATE TABLE tasks (
    id          TEXT PRIMARY KEY,
    parent_id   TEXT REFERENCES tasks(id) ON DELETE CASCADE,
    type        TEXT NOT NULL CHECK (type IN ('spec','bug','refactor','chore','enhance','security')),
    status      TEXT NOT NULL CHECK (status IN ('not_started','in_progress','in_review','done','closed')),
    title       TEXT NOT NULL,
    body        TEXT NOT NULL DEFAULT '',
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    CHECK (parent_id IS NULL OR parent_id <> id)
);
CREATE INDEX ix_tasks_parent ON tasks(parent_id);

-- One level only: a parent must itself be top-level.
CREATE TRIGGER trg_tasks_one_level
BEFORE INSERT ON tasks
WHEN NEW.parent_id IS NOT NULL
 AND (SELECT parent_id FROM tasks WHERE id = NEW.parent_id) IS NOT NULL
BEGIN
    SELECT RAISE(ABORT, 'nested sub-tasks are not allowed');
END;

CREATE TABLE task_dependencies (
    blocker_id  TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    blocked_id  TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    created_at  TEXT NOT NULL,
    PRIMARY KEY (blocker_id, blocked_id),
    CHECK (blocker_id <> blocked_id)
);
CREATE INDEX ix_task_dependencies_blocked ON task_dependencies(blocked_id);

-- Two top-level tasks, or two siblings under the same parent. Never mixed.
CREATE TRIGGER trg_task_dependencies_scope
BEFORE INSERT ON task_dependencies
WHEN (SELECT parent_id FROM tasks WHERE id = NEW.blocker_id)
     IS NOT (SELECT parent_id FROM tasks WHERE id = NEW.blocked_id)
BEGIN
    SELECT RAISE(ABORT, 'dependency must connect tasks of the same scope');
END;

CREATE TABLE agent_sessions (
    id            TEXT PRIMARY KEY,
    agent         TEXT NOT NULL,
    state         TEXT NOT NULL,
    created_at    TEXT NOT NULL,
    updated_at    TEXT NOT NULL,
    UNIQUE (agent)
);
```

Service-level invariants that SQLite cannot express cleanly:

- No dependency cycle within a scope.
- A `type=spec` task has at most one approval; approval is one-way.
- A task in `done` or `closed` has no active runs.

The one-level rule and the dependency scope rule are enforced by the two triggers above; the service still checks them first so the error message is useful.

### 8.3 Idempotency

- Webhook handlers resolve the task from the head branch and only update the status when it actually changes. Duplicate deliveries are no-ops.
- The scheduler and reconcile loop tolerate duplicate triggers.
- `create_task` is idempotent by the model's tool-call id: the chat service scans the restored `AgentSession` transcript for a previous call with the same id and returns the task it created instead of building a duplicate tree.

## 9. GitHub adapter

### 9.1 Packages and routing

- Keep `Octokit.Webhooks.AspNetCore` and `Octokit.Webhooks`. The webhook endpoint stays `MapGitHubWebhooks(secret: ...)` with the secret read from `KeyValueStore`. 
- Keep `GitHub.Octokit.SDK` for API calls, through two separate clients and two separate credentials: the App installation token and a personal access token (PAT).
- The webhook is signed, verified by the library, and accepted only for a registered project. The processor resolves `Owner`/`Name` to a project runtime; an unknown repository is logged and acknowledged.
- Every repository hook uses the same secret and payload URL.
- The route and signature behaviour do not change from the current `Program.cs`.

### 9.2 Processor

`GitHubWebhookProcessor` remains a `WebhookEventProcessor` subclass. It keeps `ProcessWebhookAsync` logging and overrides:

| Override | Events used | Effect |
|---|---|---|
| `ProcessPullRequestWebhookAsync` | `opened`, `reopened`, `synchronize` | Parse the head branch `sebastian/task-<id>` and mark the parent `in_review` if it is not already. No local PR row. |
| `ProcessPullRequestWebhookAsync` | `closed` with `Merged=true` | Parent and leaves → `done`. |
| `ProcessPullRequestWebhookAsync` | `closed` with `Merged=false` | Parent and leaves → `closed`. |
| `ProcessPullRequestReviewWebhookAsync` | `submitted`, `dismissed` | Refresh the in-memory review state cache. No status change. |
| `ProcessPushWebhookAsync` | `push` | `git.SyncMainAsync` as today. Keep the latency `shortcut:` comment. |

`ProcessIssuesWebhookAsync` is deleted. The issue label intake, `SpecTemplateValidator`, and the read path for issue bodies are retired.

Dependencies:

- The processor depends on `IProjectResolver` from `Sebastian.Libs`, the resolved project's task store, `IGitHubAppService`, and `Git`. The registry in `Sebastian.Daemon` implements the resolver; `Sebastian.Webhook` does not reference `Sebastian.Daemon`.
- The registry maps `owner/name` to one project id. The processor never scans another project's database.
- It does not depend on `JobQueue` or `ImplementSpec`. Webhook handling is a short database write, not an agent job.
- The PR branch name (`sebastian/task-<id>`) is the only recovery key; there is no local PR row to consult.

### 9.3 GitHub App service

`IGitHubAppService` keeps App authentication and exposes only:

- `GetAppAsync`
- `ListInstallationsAsync`
- `CreateInstallationTokenAsync`
- `CreatePullRequestAsync`
- `MergePullRequestAsync`
- `ClosePullRequestAsync`
- `PostPullRequestCommentAsync`
- `GetPullRequestAsync`
- `FindOpenPullRequestByHeadAsync`

Issue close and the fixed missing-sections comment are removed.

`GetPullRequestAsync` and `FindOpenPullRequestByHeadAsync` are the only sources of PR state. Nothing is cached in the database; the board calls them through a short-lived in-memory cache that webhooks invalidate.

App permissions: `Contents: read`, `Pull requests: write`, `Issues: write` (PR comments go through the issue comments API), `Metadata: read`. Git push uses the existing SSH remote; the App token never enters a git command line or log.

### 9.4 GitHub personal service

`IGitHubPersonalService` is a separate service with its own credential, the PAT, and is used only by the setup wizard and the project flows:

- `ListRepositoriesAsync` — `GET /user/repos`, so the picker shows every repository on the account, including ones the App is not installed on.
- `CreateRepositoryAsync` — `POST /user/repos` for a repository on the personal account.
- `GetRepositoryAsync` — `GET /repos/{owner}/{repo}`, used to check push permission.
- `CreateWebhookAsync` — `POST /repos/{owner}/{repo}/hooks` for the shared payload URL and secret.
- `UpdateWebhookAsync` — `PATCH /repos/{owner}/{repo}/hooks/{id}` when a hook with the same URL exists.
- `TestWebhookAsync` — `POST /repos/{owner}/{repo}/hooks/{id}/tests` for the ping check.

Rules:

- The PAT never appears in `IGitHubAppService`; the App token never appears here. The compiler enforces the split, not review discipline.
- The PAT is a classic token with the `repo` scope. Fine-grained tokens need account-level Administration and are verified in the spike before being documented as supported.
- The account service has no `IsConfigured`. Availability is `KeyValueStore.Has("github:pat")`, checked by the wizard; a call without a token throws `GitHubPersonalAuthException` with a message pointing at Settings.
- Listing repositories through the PAT does not grant the App access to them. After creating a repository, the wizard states that the App is only installed for *All repositories*, or asks the user to add the new repo under *Selected repositories*.

### 9.5 Reconcile

- Per available project runtime, on startup and every 5 minutes, list the tasks in `in_review` and resolve each one's PR from that project's origin by head branch `sebastian/task-<id>`.
- Refresh state and review state from that response and apply the same idempotent transitions as the webhook.
- Reconcile is the safety net for missed webhook deliveries. When the origin is unreachable, the board shows PR state as `Unknown` and actions that need the PR stay disabled.
- A project whose runtime is `Unavailable` is skipped until it is repaired.

## 10. Settings

There is no configuration file and no `appsettings.json`. Two stores back every setting, exposed through one `KeyValueStore` in `Sebastian.Libs`:

- **Non-secret** values use `ApplicationData.Current.LocalSettings` (the MSIX package container).
- **Secret** values use DPAPI (`ProtectedData`, `DataProtectionScope.CurrentUser`) written to `%LOCALAPPDATA%\Sebastian\secrets\<sha256(key)>.bin`.

```csharp
public sealed class KeyValueStore
{
    public bool Has(string key);
    public string? this[string key] { get; }
    public void Set(string key, string value, bool isSecret);
    public void Remove(string key);
}
```

- `isSecret` is required; there is no default.
- `Set(key, value, false)` on a key that already exists as a secret throws instead of writing plaintext.
- `Set(key, value, true)` removes any plaintext copy of the same key.
- `Has` and the indexer check the secret store first, then the plaintext store.
- A secret that fails to decrypt, for example after the profile was copied from another Windows user, reads as `Missing`; startup does not throw.

Keys:

| Key | Store | Default | Notes |
|---|---|---|---|
| `server:url` | plain | `http://127.0.0.1:5099` | Loopback only. A tunnel forwards to it. |
| `github:app-id` | plain | — | |
| `github:installation-id` | plain | — | Default installation. A project may override it. |
| `github:webhook-url` | plain | — | Public payload URL. Optional. |
| `agent:provider` | plain | — | models.dev provider id. |
| `agent:<provider>:model` | plain | — | Per provider. |
| `agent:<provider>:model:context` | plain | — | Catalog context limit at selection time. |
| `agent:<provider>:api` | plain | — | `openai` or `anthropic`; overrides the adapter inferred from `npm`. |
| `agent:<provider>:base-url` | plain | — | Optional endpoint override. |
| `agent:decision:provider` | plain | — | Provider id for the decision endpoint. |
| `agent:decision:api` | plain | — | `openai` or `systemone`. |
| `agent:decision:model` | plain | — | |
| `agent:decision:base-url` | plain | — | Optional endpoint override. |
| `github:app-key` | secret | — | PEM private key, pasted or imported once. |
| `github:webhook-secret` | secret | — | |
| `github:pat` | secret | — | Classic token, `repo` scope. Required. Used only by `IGitHubPersonalService`. |
| `agent:<provider>:api-key` | secret | — | One per configured provider. |
| `projects:order` | plain | — | Comma-separated project ids, newest last. |
| `project:active` | plain | — | Active project id. |
| `project:<id>:path` | plain | — | Absolute, normalized git top level. |
| `project:<id>:name` | plain | — | Display name. |
| `project:<id>:repo` | plain | — | `owner/name` from `origin`. |
| `project:<id>:installation-id` | plain | — | Optional per-project override. |
| `setup:step` | plain | — | Wizard resume. Removed on finish. |

Constants, not settings:

- concurrency `2` across spec, build, review, and resolve runs, shared by every project.
- PR reconcile every `5` minutes.
- verification commands `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.
- log directory `%LOCALAPPDATA%\Sebastian\logs`.

Project selection and the setup wizard are defined in `02`. The single `project:path` key is replaced by the registry keys above. After setup, a missing project list shows "Add your first project"; a missing required global key shows the wizard.

Validation:

- Every `project:<id>:path` exists, is absolute, and is the git top level.
- `SetupState.IsComplete` is derived from the required keys in `02`; there is no setup flag.
- `server:url` is loopback unless a future explicit opt-in is added.
- GitHub, provider, and decision settings are validated by the setup tests before they are written; a missing secret shows `Missing` in Settings.

Logging: source-generated `LoggerMessage` only. A small file provider writes rolling daily logs to `%LOCALAPPDATA%\Sebastian\logs`; agent output and verification output go there, not to the database.

## 11. Security

- The board has no network surface. The webhook and `/health` are the only inbound endpoints; `/health` returns a fixed string, and the webhook is signature-verified with the registered-repository and installation checks as the second gate.
- The App private key is imported once through the setup wizard or Settings and stored encrypted with DPAPI for the current Windows user. It is decrypted into memory at startup and never logged, shown, or passed on a command line. Installation tokens live in memory.
- The PAT is stored the same way and used only by `IGitHubPersonalService`: listing repositories, creating a repository, and creating or updating a repository webhook. It grants access as the user, so no other call path may use it. It is never logged or shown.
- A project boundary is a data boundary. One project's tasks, chat session, runs, and worktrees are never read by another project's services.
- The models.dev catalog and provider model lists are untrusted input. They are size-capped, parsed without reflection, displayed as data, and never used to build a command.
- The agent sandbox stays as today: no network, no `git push/reset/clean/checkout --`, no `gh`, no host-level destructive commands, scrubbed environment.
- Task and spec bodies are untrusted input. The build agent is told to treat them as requirements, not instructions. The chat agent has no shell, GitHub, or network tools; it can only create and read tasks.
- `type=security` tasks never put details in a public channel. The PR title and body are generic ("Security fix for task #12"; body references the internal task). The review agent keeps findings in the run output and log, and posts only a generic comment. The code diff is the fix and is public by nature; the prose around it is not.
- Approval and merge are human-only UI actions. Neither exists as a chat-agent tool. The review agent posts comments, not approvals.
- No auto-merge. The merge API is called only from an explicit confirmed action.

## 12. Testing decisions

The highest seam is the service layer with SQLite. Tests do not need HTTP, a real LLM, or GitHub.

| Seam | What is tested | Fakes |
|---|---|---|
| `TaskService` + SQLite | Create tree, one-level rule, same-scope dependencies, every transition in 6.2, spec gate, close semantics, idempotent transitions | Temp database per test |
| `Scheduler` | Ready-leaf rules, dependency waiting, max concurrency, one run per leaf, live-registry dedupe, retry sync | Temp database, fake agent runner |
| `GitHubWebhookProcessor` | PR opened/merged/closed, review state, duplicate delivery, unknown repository, installation mismatch, push sync | Octokit event fixtures, temp git repo, `FakeGitHubAppService` |
| `IntegrationService` | Branch creation from integration HEAD, merge success, conflict blocking, one resolve attempt, single PR | Temp git repo, `FakeGitHubAppService` |
| `IGitHubPersonalService` | List and create repository, webhook create/update/ping, missing token → typed exception, 401 expired token, 403 missing scope | `FakeGitHubPersonalService`, fake HTTP handler |
| `ProjectRegistry` | Add/remove/activate, duplicate path, normalization, missing path, corrupt order | Temp settings, temp folders |
| `ProjectRuntimeRegistry` | Runtime per project, unavailable project, migration failure, database isolation | Temp repos, temp SQLite |
| `CatalogService` | 200/304/500/timeout/corrupt/oversize/no ETag, cache reuse | Fake HTTP handler, temp folder |
| `DecisionClient` | OpenAI and System One request/response, refusal, probe | Fake HTTP handler |
| Agent roles | Prompt shape, tool calls, spec write, no GitHub tools | Fake `IChatClient` |
| Chat service | Streaming assembly, interrupted turn recovery, `create_task` validation and idempotency, session resume | Fake `IChatClient`, fake `HarnessAgent`, temp database |
| Agent session store | Serialize/restore round-trip, partial-turn state | Temp database |
| `KeyValueStore` | Secret and plain round-trip, `isSecret` routing, collision guard, corrupt secret reads as `Missing` | Temp folder, temp package data |

Rules:

- Test external behaviour and state, not private helpers.
- No test calls a real provider, GitHub, models.dev, or the network.
- Catalog tests use small fixtures with the models.dev shape; no test downloads the real document.
- Project runtime tests use one temp folder and database per project and assert that no task or session is visible across them.
- Git tests use the existing `TestRepository` pattern with a local bare remote.
- Verification tests use harmless commands (`exit 0`, `exit 1`) rather than building a real solution.
- No UI automation in v1. The board is checked manually against the acceptance list; Reactor devtools stay available in Debug.
- The AOT publish spike is a build check, not a unit test. It also executes one DbUp upgrade, one Dapper.AOT query, one Anthropic client construction, one catalog parse, and one decision serialization in the published binary, because trim warnings are suppressed rather than proven.

Acceptance criteria for this spec:

1. With setup complete and a project registered, the app creates the database and opens the board; without setup it shows the wizard; with setup and no project it shows "Add your first project". An invalid project path or a missing secret produces a readable error, not a half-working board.
2. A task tree with one parent and two sibling leaves is created through the service; a second level is rejected.
3. A speculative dependency cycle is rejected; a valid sibling dependency keeps the dependent `not_started` until the blocker is `in_review`; a top-level task blocked by another top-level task stays `not_started` even after the blocker is `in_review`, and starts only once the blocker is `done`.
4. A spec task cannot start before approval; approval moves it to `in_progress` and unblocks its children.
5. A duplicate `pull_request.closed` delivery produces exactly one `done` transition.
6. A delivery for an unknown repository or a mismatched installation is ignored.
7. After a restart, a leaf left `in_progress` with no live run shows `stalled`, its worktree and branch survive, and nothing is auto-resumed.
8. The Release publish spike completes with AOT enabled, or the fallback is recorded in this spec before implementation continues.
9. A PR closed without merge leaves the integration task and every leaf `closed`, not `done`.
10. Two registered projects run in one process with separate databases, chat sessions, tasks, and worktrees; the run cap stays at 2 across both.
11. A project with a missing path or a broken database is `Unavailable`, and the other projects still start and serve webhooks.

## 13. Out of scope

- Multi-user, signup, roles, public board.
- Multiple chat sessions or threads within one project.
- Cross-project tasks, dependencies, chat, or runs.
- Nested sub-tasks; cross-parent sub-task dependencies. Task-level dependencies between top-level tasks are in scope.
- Direct spec editing, type promotion, spec-for-other-types, reopening `done` or `closed` tasks.
- Custom columns or statuses; drag-and-drop.
- Per-leaf PRs; stacked PRs; more than one PR per parent.
- Auto-merge; auto-approval; more than one automatic conflict-resolution attempt.
- Analytics dashboard, deploy/release automation, agent evals, CVE automation.
- Run history, verification history, spec version history, and stored PR state.
- Multi-PC sync or a remote database.

## 14. Risks and open items

- **AOT + WinUI + Kestrel + Octokit + Anthropic in one binary** is the main technical unknown. The spike decides it before the rest of the work starts.
- **Kestrel inside an MSIX app** needs a firewall rule; the tunnel is an external dependency. If the app is closed, webhook delivery is lost; reconcile covers the gap.
- **Parallel integration conflicts** are expected when siblings touch the same files. The chat agent is instructed to plan disjoint scopes, but v1 does not enforce it; conflicts block and get one resolve attempt.
- **No run budget**: there is no token or wall-clock cap, so a run that hangs without issuing a shell command has no timeout. The sandbox's per-command timeout is the only guard.
- **Evidence is not persisted**: a verification failure is visible in the task detail and the log until the process restarts. The gate still blocks integration, but the exit code and output are gone afterwards.
- **Evaluation noise**: a single developer is the only user; the success metrics in the intent are outcome-level, not automated acceptance tests. The system keeps no transition history, so cycle time is not instrumented.
- **N project runtimes in one process**: each project opens its own database, scheduler, and reconcile timer. A handful is fine; v1 does not bound the number, and one broken project must not take the others down.
- **Webhook routing by `owner/name`**: one repository maps to one project. Two projects cloned from the same repository cannot both receive deliveries.
- **The catalog is a network dependency**: models.dev is on the setup path. A cache covers restarts, not a first run without a network, and the third-party schema can change.
- **The Anthropic adapter and the decision client** are AOT-verified only by the spike; both are new package or serialization surfaces.
- **Microsoft Agent Framework is new**: its API and JSON metadata change between releases. The repo pins `Microsoft.Agents.AI.Harness` 1.26.0; verify every referenced type against that package before upgrading, and re-run the AOT spike after an upgrade.

## 15. References

Microsoft Agent Framework is recent, so these upstream pages are listed explicitly. The pinned package is authoritative when a page and the package disagree.

| Topic | URL |
|---|---|
| Framework overview | <https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp> |
| Conversations and memory | <https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/?pivots=programming-language-csharp> |
| Session and serialization | <https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/session?pivots=programming-language-csharp> |
| Storage and custom `ChatHistoryProvider` | <https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/storage?pivots=programming-language-csharp> |
| `ChatHistoryProvider` API | <https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.chathistoryprovider?view=agent-framework-dotnet-latest> |
| Harness agent | <https://learn.microsoft.com/en-us/agent-framework/concepts/harness> |
| `Microsoft.Agents.AI.Harness` on NuGet | <https://www.nuget.org/packages/Microsoft.Agents.AI.Harness> |
