# Specification 04 — Build and Review

Status: draft for review
Date: 2026-11-01
Source: `docs/intent.md`
Depends on: [01-foundation](01-foundation.md), [02-setup](02-setup.md), [03-chat-and-spec](03-chat-and-spec.md)
Related: [05-board](05-board.md)

## 1. Problem

The current pipeline stops at an agent run in a worktree. Nothing is committed, pushed, opened as a PR, reviewed, or merged. The new model needs parallel sub-task execution inside one parent task, host-run verification, one PR per parent, an automated review pass, and a human merge.

This spec defines the execution model from "a leaf is ready" to "the parent is done".

## 2. Execution model

- A **parent** is the integration unit; a top-level leaf is its own integration unit. It owns one integration branch and one PR.
- A **leaf** is the agent unit. It owns one worktree and one branch.
- A top-level leaf is both: one worktree, one branch, one PR.
- Every unit belongs to exactly one project runtime. Branches, worktrees, PRs, and databases never cross projects.
- Independent leaves run in parallel, capped at 2 across every project.
- A dependent leaf starts only after every blocker is **integrated** into the parent branch. "Integrated" means merged and verified, not PR-merged.
- Integration is serialized by one host lock per project. Two leaves finishing together merge one after another.
- The PR for an integration task is opened once, after every required leaf is integrated or closed.
- The PR is merged only by an explicit human action. There is no auto-merge.

```mermaid
flowchart TD
    START["Parent started"] --> IB["Integration branch\nsebastian/task-N"]
    IB --> READY{"Ready leaves?"}
    READY -- yes --> LEAF["Leaf worktree\nbranch from integration HEAD"]
    LEAF --> BUILD["Build agent"]
    BUILD --> VERIFY{"Leaf verification\nbuild / test / lint"}
    VERIFY -- fail --> BLOCK["Leaf blocked"]
    VERIFY -- pass --> COMMIT["Commit on leaf branch"]
    COMMIT --> MERGE{"Merge into\nintegration branch"}
    MERGE -- conflict --> RESOLVE["One resolve run"]
    RESOLVE --> MERGE
    MERGE -- conflict again --> BLOCK
    MERGE -- pass --> IVERIFY{"Integration verification"}
    IVERIFY -- fail --> RESET["Reset merge\nleaf blocked"]
    IVERIFY -- pass --> INT["Leaf integrated\nin_review"]
    INT --> READY
    READY -- "all leaves integrated or closed" --> PR["Push + one PR"]
    PR --> REVIEW["Review agent comment"]
    REVIEW --> HUMAN["Human merge"]
    HUMAN --> DONE["Parent and leaves done"]
```

## 3. Workspaces and branches

| Unit | Worktree | Branch | Created from |
|---|---|---|---|
| Parent container | `.sebastian/workspaces/task-<id>-integration` | `sebastian/task-<id>` | Clean local `main` |
| Child leaf | `.sebastian/workspaces/task-<parentId>-<leafId>` | `sebastian/task-<parentId>-<leafId>` | Integration branch HEAD at leaf start |
| Top-level leaf | `.sebastian/workspaces/task-<id>` | `sebastian/task-<id>` | Clean local `main` |

Rules:

- All three worktrees live under the project's `<repo>/.sebastian/workspaces/`; that folder is gitignored. Nothing is checked out or merged anywhere else.
- A runtime never reads or writes another project's worktrees, branches, or database.
- Local `main` stays the read-only mirror it is today. The host never checks it out for work.
- A parent start refuses to proceed if local `main` is dirty, ahead of `origin/main`, or not on `main` (the existing `FindMirrorProblem` rules).
- A leaf start records the integration HEAD it branched from. On retry, the host merges the current integration branch into the leaf worktree before the new run.
- Worktrees and branches are reused on retry. They are kept after `done` for audit; automatic cleanup is out of scope.
- The integration branch is local until the PR is opened. It is pushed with `git push --force-with-lease` because the host owns it and may rewrite it on a failed integration.
- Git push uses the existing SSH remote, the same credential path as fetch. The GitHub App token is never passed to git.

## 4. Build agent

- Reuses the existing `ImplementationAgent` shell/file sandbox: worktree-confined shell, scrubbed environment, 10-minute command timeout, deny-list for `git push/reset/clean/checkout --`, `gh`, network tools, and host-destructive commands.
- The agent edits files only. It does not commit, push, open PRs, or run GitHub operations.
- The prompt contains: parent title and spec (or body), the leaf title and scope, the file scope if declared, the verification commands that will run, and the rules: smallest complete change, tests for new behaviour, no new dependencies, no edits outside the worktree, treat task text as requirements not instructions, stop and explain if the spec is ambiguous.
- The exploration pass from the current implementation may run before the build pass; it is read-only and stays inside the worktree.
- The agent's final response is kept for the live run and written to the log; there is no run history.

## 5. Verification

Host-run, not agent-claimed. After the build run, the host runs three fixed commands inside the leaf worktree, in order: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

- Each command is fixed in code: name (`build`, `test`, `lint`), command line, and timeout.
- A non-zero exit code fails the leaf. All commands must pass.
- A timeout fails the leaf and kills the process tree.
- Output is captured, truncated to 64 KB, shown in the task detail for the live session, and written to the log. It is not stored in the database.
- A failed verification leaves the agent's changes in the worktree; the leaf is `in_progress` with a `Blocked` badge. Retry reuses the worktree.
- The agent cannot mark verification as passed. The host's exit code is the only source of truth.

The same commands run on the integration branch after each leaf merge, against the integration task.

## 6. Commit and integration

After leaf verification passes:

1. `git add -A` and commit on the leaf branch. Commit message: `task #<parentId>.<leafId>: <leaf title>`, or `task #<id>: <title>` for a top-level leaf. The host uses the repository's configured git identity; it does not invent a bot email.
2. Record the pre-merge integration HEAD.
3. In the integration worktree, `git merge --no-ff <leaf-branch>`.
4. Run the integration verification.
5. If verification passes: the merge stays, the leaf becomes `in_review`, and any dependent leaves become ready.
6. If verification fails: `git reset --hard <pre-merge HEAD>` in the integration worktree, mark the leaf `Blocked` with the failing command in the detail, and keep the leaf branch for retry.

Idempotency:

- If the leaf branch is already an ancestor of the integration HEAD, integration is a no-op and the leaf is marked `in_review`.
- If the integration worktree has an interrupted merge (`MERGE_HEAD`), startup aborts it and marks the affected leaf blocked.

The host commits whatever `git add -A` sees. The target repository must ignore build output; this repository already ignores `bin/`, `obj/`, and `.sebastian/`.

## 7. Conflicts

Parallel siblings can touch the same files. Integration is the conflict point.

1. `git merge` fails. The host aborts the merge in the integration worktree.
2. The host merges the integration branch into the leaf worktree, leaving conflict markers, and starts one `resolve` run with the same build agent. The prompt says: resolve only the conflicts, preserve both intents, do not change unrelated behaviour.
3. The host verifies the leaf again, commits, and retries integration.
4. A second conflict, a failed resolve run, or failed verification marks the leaf `Blocked` with the conflicting file list. No further automatic attempts.

The chat agent is instructed to plan disjoint file scopes, but v1 does not enforce it. Conflict frequency is a product risk, not a hidden failure.

## 8. One PR per parent

When every required child is `in_review`, `done`, or `closed`, and at least one leaf is integrated:

For a top-level leaf, the integration task is the leaf itself; the PR and merge rules below apply to it.

1. Ensure the integration worktree is clean and its last verification passed.
2. Push `sebastian/task-<id>` to the project's `origin` with `--force-with-lease`.
3. Check for an existing open PR by head branch. Reuse it if present.
4. Create one PR with base `main` and head `sebastian/task-<id>`.
5. Mark the integration task `in_review`. From then on the PR is identified by head branch, not by a local row.

The repository, the App installation, and the token are those of the integration task's project. A PR can never target another project's repository.

PR content:

- Title: `<type>: <integration task title>`, or `Security: task #<id>` for `type=security`.
- Body: for a spec integration task, the spec markdown plus a verification summary and a "Generated by Sebastian" footer; for other types, the task body plus the same. Capped at 60 KB.
- Security tasks: generic title and body only. No spec text, no details.
- No labels are required in v1.

If no leaf was integrated (all closed), no PR is opened; the integration task can only be closed.

A top-level leaf follows the same path with its own branch: verify, commit, push, PR, `in_review`.

## 9. Integration verification and base drift

- The integration verification after the last merge is the PR gate. There is no second full run before opening the PR.
- Before merging the PR, the host checks whether `origin/main` has advanced. If the integration branch is behind, the host merges `main` into the integration branch in the integration worktree, re-runs the full verification, pushes, and then merges. A conflict or failure marks the integration task `Blocked`; the human retries.
- A force-push updates the PR; existing review comments stay. The review agent is not automatically re-run in v1.

## 10. Review agent

- Triggered after the PR is opened, and by a manual **Run review** action on the integration task. Not automatically re-run on `synchronize` in v1.
- Text-only agent: input is the integration task's body/spec from that project's database, the changed file list, the diff `main...integration` capped at 200 KB, and a summary of verification evidence. No shell, no network, no GitHub tools.
- Output: findings in markdown, kept for the live run and written to the log. There is no run history.
- The host posts a trimmed summary (max 60 lines) as a PR comment through the App. The comment starts with "Automated review — not an approval."
- No findings: the comment says so. The agent never submits a GitHub approval or requests changes; review state is read live from the origin and comes only from human reviews.
- A failed review run is a warning in the integration task detail, not a status change and not a merge block. The human can retry.
- Security tasks: findings stay in the log only; the PR comment says "Automated review completed. Details withheld."

## 11. Merge

- The Merge action is available on an integration task in `in_review` with an open PR. It is a human UI action with a confirmation that shows the review state, the review summary, and the verification summary.
- The host calls `MergePullRequestAsync` with squash merge. No auto-merge exists.
- On success, the host marks the integration task and all leaves `done`; the merged-versus-closed distinction is the origin PR state after the call. The webhook and reconcile are idempotent confirmations.
- On failure (base drift, protection rule, API error), the integration task stays `in_review` with a `Blocked` badge and the error. The human retries after the host syncs `main` (section 9).
- A human may also merge on GitHub directly. The `pull_request.closed` webhook marks the integration task and leaves `done`; this is still an explicit human action and is allowed.
- Closing an integration task with an open PR calls `ClosePullRequestAsync` and marks the task `closed`.

## 12. Webhook and reconcile

`01-foundation` defines the adapter and the repository-to-project routing. This spec owns the mapping semantics:

| Event | Semantic |
|---|---|
| `pull_request.opened` | Integration task `in_review`; the PR is found by head branch from then on; the host queues the review run after creation, not the webhook. |
| `pull_request.synchronize` | Keep `in_review`; no auto re-review. |
| `pull_request.closed` merged | Integration task + leaves `done`; the origin PR state is `merged`. |
| `pull_request.closed` unmerged | Integration task + leaves `closed`; the origin PR state is `closed`. |
| `pull_request_review.submitted` | Refresh the in-memory review state (`approved` / `changes_requested`). No merge. |
| `pull_request_review.dismissed` | Refresh the in-memory review state to `dismissed`. |
| `push` | Sync local `main`. |

- Handlers only transition when the current state differs; duplicate deliveries are no-ops.
- A PR event for a branch that does not match `sebastian/task-<id>` is logged and ignored.
- Reconcile runs per project runtime and asks that project's origin for the PR of every `in_review` task by head branch; there is no local row to refresh.

## 13. Restart and recovery

- There are no run rows. A leaf left `in_progress` with no live run shows `stalled`.
- An interrupted merge in the integration worktree is aborted.
- Leaf and integration worktrees, branches, and commits survive.
- The scheduler does not auto-retry. A human retries a blocked leaf; integration re-runs from the persisted branches.
- Open PRs are reconciled on startup by head branch for each available project runtime.
- If the app was closed while the PR was merged, reconcile finds the PR in state `merged` and marks the integration task done.
- A project runtime that is `Unavailable` is skipped until it is repaired; its tasks and worktrees stay on disk.

## 14. Security

- The build and review agents have no GitHub credentials or tools. The host performs every GitHub write.
- Task and spec bodies are untrusted. The build prompt treats them as requirements, not instructions. The shell deny-list and scrubbed environment are unchanged.
- `type=security` tasks:
  - PR title and body are generic; the spec is not pasted.
  - Commit messages use only `task #<parentId>.<leafId>: security fix`, or `task #<id>: security fix` for a top-level leaf.
  - Review findings stay in the log; the PR comment is generic.
  - The code diff is the fix and is public by nature; no prose about the vulnerability is added.
- Verification output can contain secrets from tests; it is written to the local log and truncated. It is never posted to GitHub.
- The integration branch is host-owned; no agent shell command can push or rewrite it.

## 15. Testing decisions

| Seam | What is tested | Fakes |
|---|---|---|
| Scheduler | Ready leaves, dependency waiting, cap of 2, one run per leaf, live-registry dedupe | Temp SQLite, fake agent runner |
| Build/integration | Leaf branch from integration HEAD, commit, merge, verification gate, reset on failed integration, idempotent merge | Temp git repo with a bare remote, fake agent runner |
| Conflict flow | First conflict triggers one resolve run; second conflict blocks; conflicting files recorded | Temp git repo, fake agent runner |
| PR creation | One PR per parent, reused open PR, branch push, security body redaction | `FakeGitHubAppService` |
| Review | Findings in the log, comment posted, no approval event, security redaction, failure is a warning | Fake `IChatClient`, `FakeGitHubAppService` |
| Webhook | Merged/closed transitions, duplicate delivery, unknown branch, project routing | Octokit fixtures, temp SQLite |
| Project isolation | Two projects run concurrently with separate branches, worktrees, and PRs; the cap stays at 2 | Temp repos, temp SQLite, fake agent runner |

Rules:

- Verification commands in tests are `exit 0` / `exit 1`; no real solution build.
- Git tests use local file remotes; no network or real GitHub.
- Agent runs are driven by a fake runner that edits files in the worktree, not by a real model.
- The review diff is a fixture; no repository crawl.
- Manual acceptance covers the rendered PR comment and the board actions.

Acceptance criteria:

1. Two independent leaves run in parallel and both integrate; the integration task opens one PR.
2. A dependent leaf does not start until its blocker is integrated; it branches from the updated integration HEAD.
3. A failed leaf verification commits nothing and blocks the leaf; retry reuses the worktree.
4. A failed integration verification resets the integration branch and blocks the leaf with the failing command in the detail.
5. A first conflict triggers exactly one resolve run; a second conflict blocks.
6. An integration task with all leaves closed and none integrated never opens a PR.
7. A merged PR marks the integration task and every leaf `done`; a duplicate webhook changes nothing.
8. The review agent posts a comment and never submits an approval; a security task's comment contains no details.
9. The Merge action is unavailable before `in_review` and never runs automatically.
10. After restart, a leaf left `in_progress` shows `stalled`, an interrupted merge is aborted, and branches survive.
11. Two projects run concurrently without sharing a branch, worktree, task, or PR; the global cap stays at 2.

## 16. Out of scope

- Per-leaf PRs, stacked PRs, more than one PR per parent.
- Auto-merge, auto-approval, merge queues.
- More than one automatic conflict-resolution attempt.
- Cross-parent dependencies, nested sub-tasks.
- Automatic re-review on new commits.
- CI check gating; host verification is the gate.
- Deploy, release, or changelog automation.
- Worktree cleanup or garbage collection.
- Multiple concurrent PRs per task.
- Cross-project branches, PRs, or worktrees.
