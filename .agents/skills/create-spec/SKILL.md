---
name: create-spec
description: Turn the current conversation into a spec plus tracer-bullet tickets and publish them as GitHub issues. No interview for the spec, quiz for the tickets.
disable-model-invocation: true
---

# Create Spec

Synthesize what was already discussed into a spec, break it into tracer-bullet tickets, then publish both to GitHub issues.

Tracker is always GitHub issues. There is no local-file fallback and no setup step.

## Process

### 1. Gather context

Work from the current conversation. If the user passes an argument (spec path, issue number, or URL), fetch it and read the full body and comments using whatever GitHub access the environment provides.

### 2. Explore the codebase

If not already done, explore the repo to understand its current state. Use the project's domain glossary vocabulary and respect ADRs in the area being touched.

Sketch the seams at which the feature will be tested. Prefer existing seams over new ones, at the highest point possible. Check with the user that the seams match expectations.

Look for prefactoring that makes the implementation easier ("make the change easy, then make the easy change"). Prefactor tickets come first.

### 3. Write the spec

Write the spec with the template below. Synthesize only, do not interview the user.

<spec-template>

## Problem Statement

The problem the user faces, from the user's perspective.

## Solution

The solution, from the user's perspective.

## User Stories

A LONG, numbered list of user stories, each in the form:

1. As an <actor>, I want a <feature>, so that <benefit>

Cover all aspects of the feature extensively.

## Implementation Decisions

Decisions made: modules built/modified, interfaces, technical clarifications, architectural decisions, schema changes, API contracts, specific interactions.

## Testing Decisions

What makes a good test (external behavior, not implementation details), which modules get tested, prior art (similar tests in the codebase).

## Out of Scope

What is explicitly out of scope.

## References

Pointers the implementer should read: quoted text, URLs, images, or any other resource. One bullet per entry with its pointer and one line on why it matters.

## Further Notes

Anything else.

</spec-template>

The spec avoids specific file paths or code snippets; they go stale fast. Tickets are the opposite: each carries the concrete contract its implementer builds from. Exception: a prototype snippet that encodes a decision more precisely than prose (state machine, reducer, schema, type shape) may be inlined, trimmed to the decision-rich parts, noted as coming from a prototype.

### 4. Draft vertical slices

Break the spec into tracer-bullet tickets.

<vertical-slice-rules>

- Each slice cuts a narrow but COMPLETE path through every layer (schema, API, UI, tests): vertical, not one layer
- A completed slice is demoable or verifiable on its own
- Each slice fits in a single fresh context window
- Prefactoring first
- Every slice carries its contract: files added or modified, symbols and signatures, API and schema changes, existing helpers to reuse
- Where two slices touch the same file, merge them or add a blocking edge (parallel worktrees merge cleanly only on disjoint files)

</vertical-slice-rules>

Give each ticket its blocking edges: tickets that must complete before it can start. A ticket with no blockers starts immediately.

Wide refactors are the exception: one mechanical change whose blast radius breaks thousands of call sites cannot land green as a tracer bullet. Sequence as expand-contract instead: expand (add the new form beside the old), migrate call sites in batches (per package/directory, each its own ticket blocked by expand, keeping CI green), contract (delete the old form, blocked by every migrate batch).

### 5. Quiz the user

Present the breakdown as a numbered list. Per ticket show title, blocked-by, end-to-end behaviour delivered, and the files and interfaces it changes. Ask:

- Granularity right? Too coarse / too fine?
- Blocking edges correct?
- Merge or split any tickets?

Iterate until the user approves. Do not publish before approval.

### 6. Publish to GitHub

Create the parent spec issue first, then the tickets in dependency order (blockers first) so edges reference real numbers:

1. Publish the parent spec issue with the spec-template body and the `ready-for-agent` label.
2. Publish one issue per ticket with the issue-template body and the `ready-for-agent` label.
3. Where the tracker supports native sub-issues, attach each ticket as a sub-issue of the parent; otherwise keep the Parent reference in the body.

Do not close or modify the parent afterwards. Work the frontier: any ticket whose blockers are all done.

<issue-template>

## Parent

Reference to the parent spec issue (`#<number>`).

## What to build

The end-to-end behaviour this ticket makes work, from the user's perspective, not layer-by-layer.

## Changes

The concrete contract for this ticket from exploration. Follow it; where the code contradicts it, report the contradiction instead of improvising.

- New files: `path/new.ts` — what it holds
- Changed files: `path/existing.ts` — what changes
- Symbols: names and signatures of new or changed functions, types, constants
- Interfaces: API endpoints with request/response shapes; schema changes and migrations
- Reuse: existing modules, helpers, or patterns to build on

Omit what doesn't apply.

## Acceptance criteria

- [ ] Criterion 1
- [ ] Criterion 2

## Blocked by

References to each blocking ticket (`#<number>`), or "None (can start immediately)".

</issue-template>
