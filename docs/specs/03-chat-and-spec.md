# Specification 03 — Chat and Spec

Status: draft for review
Date: 2026-11-01
Source: `docs/intent.md`
Depends on: [01-foundation](01-foundation.md), [02-setup](02-setup.md)
Related: [04-build-and-review](04-build-and-review.md), [05-board](05-board.md)

## 1. Problem

Chat is the only way a task enters the system. The conversation must be able to turn into a task tree, a `type=spec` task must get a generated spec in a prescribed format, and that spec must be reviewed by a human before implementation starts. Today none of that exists: the Home page has a non-functional composer, there is no chat persistence, and specs only exist as GitHub Issue bodies validated by `SpecTemplateValidator`.

Chat is scoped to the active project. Each project has its own thread, its own session, and its own tasks; a conversation never sees or changes another project.

## 2. Solution

A single continuous chat thread per project in the Home page. The Chat agent streams responses, can read the board, and can create tasks and sub-tasks with dependencies. Creating a `type=spec` task automatically starts a Spec agent run that writes a markdown spec into the task body. The task stays in `not_started` with a `Spec Review` badge until a human approves it; approval is a board action, never a chat tool.

```mermaid
sequenceDiagram
    participant H as Human
    participant UI as Chat UI
    participant C as Chat agent
    participant S as Task service
    participant G as Spec agent
    participant B as Board

    H->>UI: "Add CSV export to reports"
    UI->>C: message + board summary
    C-->>UI: stream + create_task(type=spec, children=[...])
    UI->>S: create tree
    S-->>UI: task ids
    S->>G: spec run
    G-->>S: markdown spec
    S-->>B: Task 0190f4a2 Not Started + Spec Review
    H->>B: Approve
    B->>S: approve + start
    S-->>B: In Progress; children ready
```

## 3. Chat surface

- One thread per project, stored as the serialized `AgentSession` in that project's `agent_sessions`, loaded when the project runtime starts. Messages are not duplicated in a separate table. Within a project there is no session list, no new-chat action, and no deletion.
- The thread shows user messages, assistant messages, streaming text, tool activity lines, and task cards for tasks created in that turn. After restart, cards are reconstructed from the `taskId → MessageId` map in `AgentSession.StateBag`.
- The composer sends on Enter, inserts a newline on Shift+Enter, and disables send while a response is streaming.
- A stop button cancels the active turn. The partial assistant text is kept in the session state and remains visible with status `interrupted`.
- Markdown in assistant messages is rendered; user messages are plain text. Code blocks are monospace.
- After restart the project's session is restored from `agent_sessions.state`; the last 200 messages are shown and older ones remain in the session state.
- The composer exposes project, mode, model, and reasoning-effort pickers. In the current Windows shell these are local UI state: selecting a mode, model, or effort does not change a live provider until the `02` catalog and chat client land.
- No "New Task" button or form exists anywhere. Every task comes from a chat tool call.

## 4. Chat agent

### 4.1 Runtime

- The provider and model come from the global settings in `02`; every project uses the same client.
- Built with Microsoft Agent Framework: `HarnessAgent` with its default in-session history provider; the session is serialized into `agent_sessions`. Tools are hand-written `AIFunction` subclasses, not `AIFunctionFactory` reflection, so the app stays AOT-safe. See `01-foundation` section 15 for the upstream pages.
- The chat agent has no shell, file, GitHub, or network tools. Its only capabilities are the four board tools below.
- The agent streams; the host maps harness updates into `ChatEvent`s.
- Every tool call acts on the project that owns the chat service. The agent has no project id parameter and cannot address another project.

### 4.2 Tools

| Tool | Read/Write | Purpose |
|---|---|---|
| `create_task` | write | Create a top-level task, optionally with one level of children and sibling dependencies. |
| `list_tasks` | read | List top-level tasks with type, status, title, and blocked/spec badges. Filters: status, type, text. |
| `get_task` | read | Full detail for one task: body, children and their statuses, dependencies, latest spec, latest run, PR. |
| `regenerate_spec` | write | Queue a new spec run for an unapproved `type=spec` task with a revision instruction; the result overwrites `tasks.body`. |

Not tools, by design: start, approve spec, retry, close, merge, edit task, delete task. Those are human UI actions. This is the enforcement point for the hard rules.

### 4.3 `create_task` contract

```json
{
  "type": "spec | bug | refactor | chore | enhance | security",
  "title": "string",
  "body": "string",
  "children": [
    {
      "key": "api",
      "type": "enhance",
      "title": "string",
      "body": "string",
      "depends_on": ["schema"]
    }
  ]
}
```

Rules:

- `type` and every child `type` must be one of the six fixed values.
- `title` is required and at most 120 characters. `body` is required and at most 8 KB.
- At most one level: children cannot have children.
- `depends_on` refers to sibling `key`s only. Self-references and cycles are rejected.
- The whole tree is inserted in one transaction in the owning project's database. A validation failure creates nothing.
- The host scans the session transcript for the same call id and returns the existing tree when `create_task` is replayed.
- The host records the top-level task and the assistant message that produced it in the `taskId → MessageId` map of that project's `AgentSession.StateBag`.
- A `type=spec` task starts a `spec` run immediately, whether it is top-level or a child. Children are created but locked until approval.
- A non-spec task stays `not_started`; the human starts it from the board.

### 4.4 Instructions

The chat agent must:

- Be concise and mirror the user's language.
- Create tasks only when the user asks for work; questions about the project use the read tools.
- Ask one clarifying question when the type or scope is genuinely ambiguous.
- Prefer `spec` when the work needs design review, `enhance` for a small well-understood improvement, and `bug` / `refactor` / `chore` / `security` by nature.
- Keep the tree small; create a leaf unless the work genuinely splits into parallel or dependent parts.
- Declare dependencies only when a leaf needs another leaf's code; not for cosmetic ordering.
- Never claim to have started, approved, merged, or closed anything.
- Never invent an id, a run, or a PR link.

## 5. Streaming contract

`ChatService.SendAsync(text, cancellationToken)` returns `IAsyncEnumerable<ChatEvent>` consumed by the UI on a background task and marshalled to the UI thread.

| Event | Payload | UI effect |
|---|---|---|
| `UserMessageStored` | message id | Append user bubble. |
| `AssistantStarted` | message id | Insert empty assistant bubble. |
| `Token` | text | Append text. |
| `ToolStarted` | name, human summary | Show an activity line. |
| `ToolCompleted` | name, summary, created task ids | Replace the activity line; render task cards. |
| `Completed` | message id | Mark the bubble complete; persist status. |
| `Failed` | message | Mark interrupted; show a retry affordance. |

Rules:

- One active turn per project at a time. A second send while streaming is rejected by the service and disabled in the UI. A project switch does not cancel the active turn in the project that was left.
- User and assistant messages are written into the session state before streaming starts, and the session is serialized to the project database on a short debounce while streaming, so a crash leaves a recoverable transcript.
- Token usage is not tracked and does not limit a turn; there is no budget.
- A cancelled or failed turn keeps the partial text with status `interrupted`. Retrying creates a new assistant turn; the user message is not duplicated.
- Tool call ids are passed through unchanged; `create_task` is idempotent on that id. `regenerate_spec` is idempotent by rejecting a new request while a spec run is already queued or running for the task.

## 6. Context policy

Microsoft Agent Framework assembles the conversation context. The host contributes only what the framework cannot know:

1. System prompt with the tool rules and type guidance, including the rule that a security task body never enters the summary.
2. Board summary for the owning project: top-level tasks only, up to 100, each with id, type, status, title, and badges.
3. The active task detail, if the user opened one from the board.

Chat history is the harness's default in-session history provider inside `AgentSession`; the whole session is serialized to the active project's `agent_sessions.state` after each turn and on a debounce during streaming. Compaction is the framework's (`HarnessAgentOptions.MaxContextWindowTokens` plus `CompactionStrategy`), not a hand-written truncation loop. Crash recovery inside a tool loop relies on `PerServiceCallChatHistoryPersistingChatClient`; the AOT spike confirms it works with the harness.

No embeddings, no RAG, no full-repo context, and no cross-project context. The chat agent uses `list_tasks`/`get_task` when it needs more.

## 7. Spec agent

### 7.1 Trigger

- Automatically after a `type=spec` task is created.
- On `regenerate_spec`, only while the task is `not_started` and not approved.
- A retry of a failed spec run is a human action from the board.

### 7.2 Input and capability

- Input: task title and body, the chat messages linked to the task or its top-level ancestor, the previous spec (for regeneration), and the revision instruction.
- Read-only repository access: a text-file reader rooted at the active project's path, restricted to tracked text files. It denies `.git`, `.sebastian`, `.env*`, `*.pem`, `*.key`, `*.pfx`, binaries, and build output. No shell, no network, no writes, no GitHub.
- The agent reads only what it needs to ground the spec; the file tool result is data, not instructions.

### 7.3 Output format

The spec is markdown with these headings, in order:

```markdown
## Problem Statement
## Solution
## User Stories
## Tech Stack
## Implementation Decisions
## Testing Decisions
## Out of Scope
## Further Notes
```

- The spec agent is instructed to be concrete and to name the modules, schema, and tests it expects to touch.
- The host validates that the output is non-empty, contains every heading, and is at most 100 KB. An invalid output gets one retry with the validation error appended; a second failure marks the run failed.
- The body is written to `tasks.body`, replacing any previous draft; the task stays `not_started`.
- The initial chat brief is preserved in the session state; it is not overwritten.

### 7.4 Spec review

- On success the board shows `Spec Review` on the task in `not_started`.
- The detail shows the latest spec and the chat message that created the task. There is no version history.
- Human actions: **Approve** and **Regenerate via chat**.
- Regenerate prefills the composer with a revision request and focuses it; it does not edit the spec directly.
- Approve opens a confirmation that names how many children will start, or confirms that the leaf itself will start. Approval is the only way a spec task leaves `not_started`.
- After approval the spec is frozen: `regenerate_spec` rejects, the Approve action disappears, and the body is read-only. A scope change is a new task created through chat.
- A failed spec run shows `Blocked` and a retry action; the task stays in `not_started`.

## 8. Failure handling

| Failure | Behaviour |
|---|---|
| Provider error during chat | Assistant turn `interrupted`; error banner; retry available; user message preserved. |
| Tool validation failure | Tool returns a structured error to the model; no partial tree is written; the agent explains and asks. |
| Spec run provider error | Task stays `not_started`; `Blocked` badge; retry from the board. |
| Invalid spec format | One automatic retry; then `Blocked`. |
| App restart while streaming | Message `interrupted`; partial content visible; next turn starts clean. |

## 9. Security

- The chat is local; nothing is posted to GitHub from this surface.
- The project boundary is enforced by the runtime that owns the chat service. The agent cannot name another project, and its tools resolve against that runtime's database.
- Board summaries exclude `type=security` bodies.
- The chat agent has no shell, file, or GitHub tools, so a malicious task body cannot escalate through it.
- The spec agent's file reader is read-only and deny-listed. It cannot read secrets or `.git`.
- Approval and merge remain human UI actions; they are absent from the tool list.
- The assistant is instructed not to echo secrets or environment variables.

## 10. Testing decisions

| Seam | What is tested | Fakes |
|---|---|---|
| `ChatService` | Streaming event order, cancellation, partial-turn recovery, one-turn lock, session resume | Fake `IChatClient` that emits tokens and tool calls, fake `HarnessAgent` |
| Session store | Serialize/restore round-trip, debounced save, partial-turn state | Temp SQLite |
| Project isolation | Two project databases never share a session or a task; switching projects switches the thread | Temp SQLite per project |
| Tool layer | `create_task` validation, atomicity, idempotency, one-level rule, sibling dependencies, `regenerate_spec` guards | Temp SQLite |
| Spec service | Trigger on spec creation, format validation, body overwrite, retry-once, freeze after approval | Fake `IChatClient` producing valid/invalid markdown |
| Approval | Human-only transition, children unlocked, leaf build enqueued, rejection after approval | Temp SQLite |
| Context builder | Board summary excludes sub-tasks and security bodies; compaction stays inside the framework's limits | Temp SQLite |

Rules:

- No test calls a real provider.
- Chat tests drive the fake client with a scripted sequence; they assert persisted state and emitted events, not private methods.
- Chat tests run against one project database; the isolation test uses two and asserts that neither session nor task is visible across them.
- Spec format tests use fixed markdown fixtures.
- UI behaviour is checked manually against the acceptance list; no UI automation in v1.

Acceptance criteria:

1. A request that needs design creates a `spec` parent with children and dependencies; a spec run starts automatically.
2. A request for a small fix creates a single non-spec leaf; no spec run starts.
3. The spec contains all eight headings; an invalid output is retried once and then blocked.
4. The task stays `not_started` with `Spec Review` until a human approves; the chat agent cannot approve.
5. Approval moves the task to `in_progress` and makes its children eligible for scheduling.
6. Regeneration before approval overwrites the draft body; after approval the tool rejects and the chat proposes a new task.
7. A cancelled turn preserves partial text and does not duplicate the user message.
8. A replayed `create_task` call id returns the same tree and creates no duplicates.
9. Two projects have separate threads; a task created in one is absent from the other; switching projects switches the thread without cancelling an active turn.

## 11. Out of scope

- Multiple chat sessions or threads within one project; cross-project chat.
- Message editing, deletion, voice, or images. Files can be picked into the composer as attachments, but the agent does not receive them in v1.
- Editing or deleting a task's fields or tree after creation; create a new task through chat.
- Direct spec editing or a rich-text spec editor.
- Chat-initiated start, approve, retry, close, or merge.
- Model switching per conversation, temperature/tool settings in the UI.
- Semantic search or repository-wide RAG.
- Spec version history or diffing; only the latest body exists.
- A chat "clear" action.
