# Specification 05 — Board

Status: draft for review
Date: 2026-11-01
Source: `docs/intent.md`
Depends on: [01-foundation](01-foundation.md), [02-setup](02-setup.md), [03-chat-and-spec](03-chat-and-spec.md), [04-build-and-review](04-build-and-review.md)

## 1. Problem

The current Windows shell has a non-functional Home composer, a placeholder Kanban page, and a fake project list. There is no way to see tasks, specs, runs, evidence, or PR state, and no place to approve a spec or merge.

This spec defines the board surface: navigation, projects, columns, cards, detail, actions, settings, and states. Chat behaviour is defined in `03`; build behaviour in `04`; setup, projects, and models in `02`. The visual treatment of sub-tasks follows the existing prototype; this spec fixes the data and actions that prototype must expose, not its final layout.

## 2. Navigation

| Nav item | Route | Content |
|---|---|---|
| Home | `Route.Home` | Chat thread for the active project (section 6) |
| Kanban | `Route.Kanban` | Kanban for the active project (section 3) |
| Projects | `Route.Projects`, `Route.Project(Guid Id)` | Project list, add, switch, remove (section 8.1) |
| Settings | `Route.Settings` | Global and active-project settings (section 8) |

- The `Projects` section lists the registered projects from `ProjectRegistry`. The fake `sebastian` and `acme-web` entries are replaced by real projects. The active project's name and repository are shown in the title bar.
- Selecting a project sets `project:active` and opens Home for that project. Runs and chat turns in the previous project continue in the background.
- A task opens `Route.TaskDetail(int id)`. While a task detail is open, the Kanban nav item stays selected.
- The setup wizard is `Route.Setup`; it has no navigation pane while setup is incomplete.
- The existing title bar, pane, theme resources, and NavigationView wiring stay. Pages remain pure C# Reactor components under `Sebastian.Windows/Pages`.

## 3. Kanban

Four fixed columns, in order:

1. **Not Started**
2. **In Progress**
3. **In Review**
4. **Done**

Rules:

- Columns are fixed. No custom columns, no drag-and-drop.
- The board always shows the active project's database. There is no combined cross-project view.
- Tasks in `closed` appear in no column unless the `Show closed` toggle is on. The toggle sits in the filter bar and defaults to off, so a cancelled task does not clutter the board.
- The board's columns show top-level tasks. Sub-tasks are available inside the parent card and the detail. The existing prototype defines whether children also appear as cards, expand inline, or stay as a checklist; that presentation is deferred, but the data contract in this spec is fixed.
- Each column is independently scrollable; the board scrolls horizontally if the window is narrow.
- Sort by `updated_at` descending. The Done column lists only `done` tasks, loads the latest 50, and offers "Show more"; the other columns load all.
- No WIP limits.
- Status changes happen through actions, never by moving a card.

### 3.1 Card

A card shows:

- Type badge: `spec`, `bug`, `refactor`, `chore`, `enhance`, `security`.
- Title.
- `Spec Review` badge when the task is a `spec` task in `not_started` with a generated spec awaiting approval.
- `Generating spec` while the spec run is active.
- `Blocked` badge with a short reason from the latest failed run, verification, integration, or PR delivery.
- PR badge with state (`Open`, `Approved`, `Changes requested`, `Merged`, `Closed`) when a PR is linked.
- Sub-task progress (`2/3`) when the task has children. The prototype decides whether this is a count, a bar, or a list preview.
- Relative age from `updated_at` (e.g., `2h`).
- A `Closed` badge on a `closed` task, shown only when the `Show closed` toggle is on.

A card is a full-width button with an accessible name built from type, title, status, and badges. Color is never the only status signal; every badge has text or an icon.

## 4. Filters

- Type filter: multi-select over the six types, default all. This is a filter, not a separate board.
- Text filter: case-insensitive match on title and task id.
- `Show closed` toggle, default off. When on, `closed` tasks are included; when off, they appear in no column.
- Filter state is in-memory; it resets on app restart and when the active project changes.
- An empty filter result shows "No tasks match" with a clear-filter action.
- The filter bar shows the count per status and a "Clear" action.

## 5. Task detail

A full-page detail (`Route.TaskDetail`) with a back action to the board. Sections:

### 5.1 Header

- Task id (short form: the first 8 characters of the UUIDv7), type badge, status, title.
- Primary actions, enabled by state:
  - **Start** — non-spec task in `not_started`.
  - **Approve spec** — spec task in `not_started` with a generated spec.
  - **Retry** — leaf with a `Blocked` badge.
  - **Run review** — integration task in `in_review` with an open PR.
  - **Merge** — parent in `in_review` with an open PR.
  - **Close** — any task that is not `done` or `closed`.
  - **Open PR** — when the origin reports a PR for the task branch.
- Actions that are unavailable are hidden, not just disabled. A disabled state with a reason is used when the action exists but a prerequisite is missing.

### 5.2 Body

- Render markdown. For a `spec` task, this is the latest spec; for other types, the task description.
- Edit is not available. Scope changes go through chat.
- A `spec` task shows only the latest body; there is no version history.
- A `spec` task in `not_started` shows `Approve spec` and `Regenerate via chat`. Approve opens a confirmation naming how many children will start. Regenerate focuses the chat composer with a revision request.
- After approval, the body is read-only and the approve/regenerate actions are gone.

### 5.3 Sub-tasks

The prototype defines the layout. The detail must expose, for every child:

- Title, type, status.
- `Blocked` badge and reason.
- `Waiting on <short id>` for unmet dependencies.
- `Integrated` when `in_review`.
- The child's live run and verification summary, if one is active in this process.
- The ability to open the child's own run detail.

Progress (integrated / total) is shown in the header or the section header.

### 5.4 Runs and evidence

- Only the run that is live in this process is shown: kind (`spec`, `build`, `review`, `resolve`), status, duration, model, start time.
- Run detail shows the agent's final output, the error, the worktree/branch, and the chat message that created the task.
- Verification shows scope (`leaf`, `integration`), kind (`build`, `test`, `lint`), command, exit code, and duration while the session is alive. After a restart only the last failure shown in the task detail remains; the full output is in the log file.
- Verification output is collapsible, monospaced, and truncated.
- Failed verification is the first thing shown in a blocked task.

The detail says explicitly that run and verification history is not kept, so a restart does not look like lost data.

### 5.5 Pull request

- Read live from the origin repository by head branch `sebastian/task-<id>`: link, state, review state, and when it was last fetched.
- When the origin is unreachable the state shows `Unknown`, and Merge and Close stay disabled.
- Review agent findings from the current review run, clearly labelled "Automated review — not an approval."
- Merge confirmation shows the live review state, the review summary, and the integration verification result from this session. If no human approval is recorded, the confirmation says so but does not block; the merge is still an explicit human action.
- For `type=security`, the PR body and review comment are generic; the detail still shows the local spec and findings.

## 6. Chat page

The Home page becomes the chat thread defined in `03`, scoped to the active project.

- Message list with user and assistant bubbles, streaming text, tool activity lines, and inline task cards.
- Composer at the bottom: Enter sends, Shift+Enter inserts a newline, send is disabled while streaming, stop is available while streaming.
- The model chip selects the model and reasoning effort (`Low`–`Max`). The choice is local UI state until the `02` catalog and chat client land.
- Empty state uses the existing prompt ("Describe a spec, bug, or refactor…") and no task form.
- A task card opens `Route.TaskDetail`.
- Error banners are dismissible and offer retry.
- The page auto-scrolls to the newest message unless the user has scrolled up; a "Jump to latest" affordance appears in that case.
- The thread is the active project's only chat surface. There is no session list and no new-chat button. Switching projects switches the thread; an active turn in another project keeps running.

## 7. Actions and guards

| Action | Visible when | Guard | Confirmation |
|---|---|---|---|
| Start | Top-level task `not_started`, not a spec awaiting approval | Mirror `main` clean; container has children; no active run. Child leaves start from the scheduler, not this action. | No |
| Approve spec | `spec` task `not_started`, latest spec run succeeded | Not already approved; human action; for a child, parent `in_progress` | Names the children that will start, or the leaf build |
| Retry | Leaf with `Blocked` | Latest run/verification failed or the leaf is stalled | No |
| Run review | Integration task `in_review` with open PR | PR open | No |
| Merge | Integration task `in_review` with open PR | PR open; no auto-merge | Shows review state and evidence |
| Close | Any task that is not `done` or `closed` | Confirmation; parent closes non-terminal children and its PR | Confirmation only; no reason is stored |
| Open PR | Origin reports a PR | — | Opens the browser |

- All actions call the service and wait for the resulting event before updating the UI. Buttons disable while pending.
- Errors are shown in a dialog or inline banner with the underlying message.
- Start, approve, retry, close, and merge are human-only; no chat tool exposes them.

## 8. Settings and projects

An editable form, not a read-only summary. Values are read and written through `KeyValueStore`; secrets never render their value. The fields and validation rules are defined in `02`; this section fixes the layout and the actions.

### 8.1 Projects

- The Projects navigation lists every registered project with its name, repository, and status (`Ready` or `Unavailable`), and marks the active one.
- **Add project** opens the flow from `02` section 8.2: Open local, Clone from GitHub, or Create new repository.
- Clone and Create are disabled when the PAT is missing or rejected, with a link to the token field.
- After creating a repository, the flow states that the App must cover it: it is available immediately under *All repositories*, otherwise it must be added under *Selected repositories*.
- Selecting a project sets `project:active` and opens Home for that project. Runs and chat turns in the previous project continue.
- **Remove** requires confirmation, is refused with an active run or chat turn, and leaves the folder and database on disk.
- **Create webhook** and **Send test delivery** are available per project; the last delivery and last reconcile times are shown.
- An `Unavailable` project shows the error and offers Recheck and Remove; its board is not reachable.

### 8.2 Global settings

**Server**

- `server:url`, default `http://127.0.0.1:5099`, loopback only.

**GitHub**

- App ID and default installation ID, editable.
- Private key and webhook secret: `Configured` / `Missing` with **Import** / **Replace** / **Remove**. The key is pasted or imported from a file once; the path is not stored.
- Personal access token (`repo` scope): `Configured` / `Missing` with **Set** / **Replace** / **Remove**. The value is write-only and is used for repository listing, creation, and webhooks.
- Webhook payload URL, editable; changing it requires re-running **Create webhook** for every project. The path is `/api/github/webhook`.

**Agent**

- Provider, API type, base URL, model, and the catalog metadata for the selected model: context, output, cost, and `tool_call`.
- **Change model** opens the model picker; only `tool_call: true` models are listed.
- API keys are write-only rows with `Configured` / `Missing` and **Set** / **Replace** / **Remove**.
- Decision provider, API shape, model, and key status.
- **Refresh catalog** revalidates the models.dev document with its ETag and shows the result.
- **Run setup again** opens the wizard prefilled.

**Fixed values**

- Concurrency `2`, reconcile every `5` minutes, verification commands `dotnet build` / `dotnet test` / `dotnet format --verify-no-changes`, log directory `%LOCALAPPDATA%\Sebastian\logs`.
- App version and AOT/trimmed publish mode.

### 8.3 Project settings

- Name, path, repository, installation override, and database path for the active project.
- Webhook hook id, last delivery, and last reconcile time.
- **Open folder** in Explorer; **Remove project**.

### 8.4 Test gate

- Replacing the PAT, App credentials, a provider key, the agent model, or the decision model requires the test defined in `02` section 5.9.
- The new value is written only after the test passes; on failure the old value stays and the error is shown.
- Changing the server URL asks for a restart. Adding or removing a project takes effect immediately; a project switch never restarts the host.

## 9. State and refresh

- Services emit in-process change events per project runtime: task created/updated, run updated, chat message updated, PR updated, webhook health changed.
- The board and detail subscribe to the active runtime and refresh the affected query. No polling of the database for normal updates.
- A 30-second safety refresh runs while the board or detail is visible, covering missed events. It also re-reads PR state from the origin for the active project's `in_review` tasks.
- The chat page refreshes from the streaming events, not the database.
- No optimistic updates; the UI reflects committed state.
- An action result that arrives while the user navigated away is ignored; the next view loads the current state.
- Switching projects cancels no run and no chat turn. The new view loads that project's state; the old runtime keeps working in the background.

## 10. Error and degraded states

| State | UI |
|---|---|
| Setup incomplete | The setup wizard from `02`; the board does not open. |
| No project registered | "Add your first project" with Open local, Clone from GitHub, or Create new repository; the board does not open. |
| Project unavailable | Full-page error for that project with the path and failure; Recheck and Remove; other projects still work. |
| PAT missing or rejected | The project flows are disabled with "Add a personal access token"; an expired token shows the API error verbatim. |
| Catalog offline | Setup and model pickers show the cached catalog and its last check time; a missing cache blocks the wizard with Retry. |
| Database unavailable | Full-page error with the path and the failure; retry. |
| Webhook host failed at startup | The app shows the fatal host error from `01-foundation`; the board does not open. |
| Webhook delivery stale | Board banner: "Webhook delivery is stale; PR status refreshes every N minutes." Detail still works. |
| GitHub unreachable | PR state shows `Unknown`; Merge, Close, and Open PR are disabled with the underlying error. |
| Model/provider error | Assistant message or run shows the error; retry where applicable. |
| Git error | Blocked badge and the command error in the task detail. |
| Empty board | Per-column empty copy, not a blank pane. |
| No spec yet | "Generating spec…" then the spec or a blocked retry. |

## 11. Accessibility and theming

- Light, dark, and system themes follow the existing shell resources.
- Minimum window size: 1100×720. The board scrolls horizontally below its natural width.
- Keyboard: Tab reaches cards, filters, and actions; Enter/Space activates; Escape closes dialogs; Enter sends chat, Shift+Enter adds a newline.
- The Projects list and the active-project indicator are reachable by keyboard; the active project is announced by screen readers.
- Every icon-only button has an accessible name; status badges have text; focus is visible.
- Text contrast follows the theme's semantic brushes; no hard-coded colors.
- Cards are buttons, not clickable borders, so screen readers get a role and name.
- The chat log is a list with live-region announcements for new assistant text, throttled to avoid noise.

## 12. Implementation notes

- Reactor components only: no XAML, no code-behind, no ViewModels, no data binding. State via hooks; queries via services.
- Routes: `Route.Home`, `Route.Kanban`, `Route.TaskDetail(int Id)`, `Route.Projects`, `Route.Project(Guid Id)`, `Route.Settings`, `Route.Setup`. `Route.Project(string Name)` is replaced by the id-based route.
- The shell resolves the active runtime from `ProjectRuntimeRegistry`. Pages never hold a database connection; they call the runtime's services.
- Board data comes from a `BoardService` query for the active project that returns top-level tasks with derived badges and child progress. If the prototype shows child cards, the same query includes children with their parent id. The detail comes from one `TaskDetailService` query.
- Markdown rendering uses the Reactor Markdown component; add `Microsoft.UI.Reactor.Advanced` if the AOT spike confirms it. If the package cannot be AOT-published, use a minimal headings/lists/code renderer in the app.
- The board does not query SQLite directly; it calls services. Derived badge logic lives in the service and is unit-tested.
- The prototype's sub-task layout is the reference for section 5.3; the data contract above is fixed even if the layout changes.

## 13. Testing decisions

| Seam | What is tested | Fakes |
|---|---|---|
| Board query | Column grouping, top-level default, optional child cards, sorting, Done cap, filter, search | Temp SQLite |
| Badge derivation | Spec Review, Generating, Blocked reason, PR state, child progress | Pure functions over fixtures |
| Action guards | Start/approve/retry/close/merge preconditions and confirmation data | Temp SQLite, fake services |
| Task detail query | Runs, verification, PR from origin, spec body | Temp SQLite, `FakeGitHubAppService` |
| Chat UI state | Streaming append, stop, interruption, task card creation, auto-scroll | Fake `ChatService` |
| Settings form | Secret status via `Has`, validation errors, restart prompt, catalog refresh | Fake `KeyValueStore` |
| Project navigation | Add/switch/remove, active indicator, unavailable project, no-project empty state | Fake `ProjectRegistry`, temp runtimes |
| Settings test gate | Test before write, old value on failure, masked secrets | Fake `KeyValueStore`, fake tests |

Rules:

- No UI automation in v1. The UI is checked manually against the acceptance list; Reactor devtools stay available in Debug.
- Pure derivation functions (badges, filters, progress) are unit-tested without a UI.
- Service queries are tested against a temp database.
- Manual checks cover theming, keyboard, accessible names, narrow window, and degraded banners.

Acceptance criteria:

1. The board shows four fixed columns and the prototype's chosen sub-task presentation; sub-task status is always available from the card or detail.
2. Type and text filters combine; clearing them restores the full board; the Done column caps at 50 with "Show more"; the `Show closed` toggle reveals and hides `closed` tasks.
3. A spec task in `not_started` shows `Spec Review`; approving it after confirmation moves it to In Progress and unlocks its children.
4. A blocked leaf shows the reason and a Retry action; a parent shows the badge when any child is blocked.
5. The detail shows the live run, the verification result from this session, the PR state read from origin, and review findings.
6. Merge is visible only for an `in_review` integration task with an open PR and always asks for confirmation.
7. The Home page is the active project's chat thread; there is no New Task button.
8. Streaming text, stop, interruption, and inline task cards work.
9. Light/dark themes, keyboard navigation, and accessible names are correct.
10. A stale webhook (missed deliveries) shows the degraded banner; the board still loads and reconciles.
11. Settings persist across restarts through `KeyValueStore`; an imported secret shows `Configured` and its value is never rendered; removing it shows `Missing` again.
12. The repository picker lists the account's repositories and a new personal repository can be created and cloned; a rejected PAT shows the API error and keeps the previous value.
13. The Projects list adds, switches, and removes projects; the active project is marked; removing one leaves its folder and database intact.
14. Two projects show different boards and threads; switching does not cancel work in the other project.
15. A model or secret replacement is refused when its test fails; the old value still works.
16. Setup incomplete opens the wizard; complete setup with no project opens "Add your first project".

## 14. Out of scope

- Drag-and-drop, custom columns, swimlanes, WIP limits.
- Analytics dashboard, charts, cycle-time trends, burndown.
- OS notifications, email, or push.
- Cross-project boards, combined task lists, or drag-and-drop between projects.
- Editing credentials or models without a passing test.
- Auto-installing the GitHub App or creating a tunnel.
- Mobile or web board.
- Editing a task title/body or tree from the board or chat; create a new task instead.
- Spec version history or diffing; only the latest body exists.
- Bulk actions.
- The final visual layout of the sub-task section; that follows the existing prototype.
