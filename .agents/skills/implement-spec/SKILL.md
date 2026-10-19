---
name: implement-spec
description: Implement a spec's tickets on one integration branch, using parallel worktrees when the frontier allows, closing with code review and a pull request.
disable-model-invocation: true
---

# Implement Spec

Implement every ticket of a spec on a single integration branch, then push it and open a pull request whose body follows the `pr` skill.

The tickets are not a list of steps. They are a task graph with blocking relationships between them, so there is always a frontier of tickets ready to be grabbed: tickets whose blockers are all done. Tracker is always GitHub issues. There is no setup step.

## Steps

1. Read the spec and tickets to understand the task graph. If the user passes an argument (spec issue number or URL), fetch it and read the full body and comments using whatever GitHub access the environment provides.

2. Create the integration branch off the default branch.

3. Work the frontier. When the frontier holds exactly one ticket (including the case of a single-ticket spec), implement it directly on the integration branch with no worktree. When the frontier holds more than one ticket, give each its own worktree on its own branch and run one implementer subagent per ticket in parallel, each confirming its worktree is based on the integration branch tip before starting.

4. Keep communication to and from subagents sparse. Communicate primarily through context pointers: to the spec, tickets, and previous commits. Do not duplicate information already available via pointers.

5. As each ticket completes, merge its work into the integration branch, then recompute the frontier and continue under step 3: go parallel again when the frontier grows past one, stay direct when it shrinks to one.

6. Prove each ticket the cheap way. Pick the cheapest feedback loop that answers whether the ticket's behaviour works, and keep a regression guard that fails on the wrong behaviour, not on a missing symbol.

7. Once all tickets are complete, invoke the `code-review` skill against the integration branch. Hand the findings to a fresh fixer subagent — a new session, briefed with the findings and the diff pointer only, never the implementers' context, so it reads the code without their bias — and let it fix them. Report anything the fixer leaves open instead of looping.

8. Push the integration branch and open a pull request whose body follows the `pr` skill template. Resolve each ticket the way the issue tracker closes work, and report the integration branch and the pull request.

9. Clean up all implementer worktrees.
