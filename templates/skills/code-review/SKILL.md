---
name: code-review
description: "Review the changes since a fixed point (commit, branch, tag, or merge-base) along two tracks: Spec (does the code match what the originating issue/spec asked for?) and Quality (reviewed with the skill's embedded quality prompt, plus any stack-specific review skill the environment provides, e.g. winui-code-review). Both tracks run as parallel sub-agents; the reports are presented side by side, never merged. Use when the user wants to review a branch, a PR, work-in-progress changes, or asks to \"review since X\"."
---

Review of the diff between `HEAD` and a fixed point the user supplies, along two tracks:

- **Spec**: does the code faithfully implement the originating issue / spec?
- **Quality**: will it hold up — logic, safety, load, tests on risky code, speed, and whether every line is needed. Reviewed with the embedded quality prompt below, plus any stack-specific review skill the environment provides that matches the project (e.g. `winui-code-review` for a WinUI project).

Both tracks run as **parallel sub-agents** so they don't pollute each other's context. This skill aggregates their reports.

Tracker is always GitHub issues. There is no setup step.

## Process

### 1. Pin the fixed point

Whatever the user said is the fixed point (a commit SHA, branch name, tag, `main`, `HEAD~5`, etc.). If they didn't specify one, ask for it.

Capture the diff command once: `git diff <fixed-point>...HEAD` (three-dot, so the comparison is against the merge-base). Also note the list of commits via `git log <fixed-point>..HEAD --oneline`.

Before going further, confirm the fixed point resolves (`git rev-parse <fixed-point>`) and the diff is non-empty. A bad ref or empty diff should fail here, not inside two parallel sub-agents.

### 2. Identify the spec source

Look for the originating spec, in this order:

1. Issue references in the commit messages (`#123`, `Closes #45`), fetched using whatever GitHub access the environment provides.
2. A path the user passed as an argument.
3. If nothing is found, ask the user where the spec is. If they say there isn't one, the **Spec** sub-agent will skip and report "no spec available".

### 3. Spawn both sub-agents in parallel

Issue both sub-agent calls together, in the foreground, and aggregate the reports they return.

**Spec sub-agent prompt** should include:

- The diff command and commit list.
- The path or fetched contents of the spec.
- The brief: "Report: (a) requirements the spec asked for that are missing or partial; (b) behaviour in the diff that wasn't asked for (scope creep); (c) requirements that look implemented but where the implementation looks wrong. Quote the spec line for each finding. Under 400 words."

If the spec is missing, skip the Spec sub-agent and note this in the final report.

**Quality sub-agent prompt** should include:

- The diff command and commit list, filled into the example prompt's two placeholders.
- The example quality prompt below, passed verbatim.

```text
Quality-review the change below.

First: load any review skill in your environment that matches this project's stack
(e.g. `winui-code-review` for a WinUI project) and apply it on top of this prompt.
Skip `code-review` itself. State which skills you loaded. Do not modify any files.

The change:
- Diff command: <diff command>
- Commits: <commit list>

Read the diff, then the code it touches: callers of every changed function, the
functions it calls, its tests, the README. Find the expected load in the repo
(README, deploy config) and say which load you assumed. If the change is oversized
(roughly >1000 changed lines), say so and review the highest-risk files first.

Check, in this order: correctness → safety → load → tests → speed → lean.

1. Bug: wrong result, crash, missed edge case (empty, zero, last item, rounding,
   time zones), a caller broken by the change, a fix applied in one caller while
   the shared function stays broken.
2. Risk: security holes (injection, weak randomness, secrets, missing checks on
   external input), data loss (swallowed errors, wrong write order, no transaction).
3. Load: check-then-write races, work every process repeats that must be shared
   once, lists or memory that only grow, a query per item.
4. Missing test: risky new logic (a branch, a parser, money, security, data writes,
   a bug fix) with no test that fails when it breaks. Where a test looks thin, name
   the scenario that would stay green if the logic broke.
5. Structure: a refactor that relocates complexity instead of removing it; feature
   logic leaking into a shared module; a conditional bolted onto an unrelated flow;
   a function mixing unrelated jobs.
6. Dependencies: a new dependency the standard library or an existing helper already
   covers; an upgrade merged without its changelog read; a bulk bump that hides cause
   and revert (one dependency per change; review the lockfile diff).
7. Lean: dead code, unused options, an abstraction with one implementation, a
   near-copy of an existing helper, config nobody sets, lines that should not exist.

Rules:
- Every finding needs a concrete case: "this input/situation → this wrong result".
  No case, no finding.
- No style taste, no "consider", no vague worries.
- Propose the smallest fix that works; prefer fixes that delete code.
- A `shortcut:` comment that names its limit is a decision, not a finding.

Output, no preamble:
- Start with "What this change does:" in 2-3 sentences.
- Findings, numbered across three groups (skip empty groups): **Must fix** (bug,
  security, data loss, breaks at the assumed load) · **Should fix** (risky code
  without a test, real slowness, duplication, a function mixing jobs, a dependency
  that did not earn its place, code that should not exist) · **Nice to have**
  (small speed-ups, shorter forms).
- Each finding: what it is, the problem with its concrete case, the smallest fix,
  and what happens if we skip it.
- End with `Verdict: ship.` or `Verdict: fix <numbers> first.`, plus `Not checked:`
  if something mattered and you could not check it.

If nothing is wrong: "What this change does:", then "Looks good. Ship." and one
line on what you checked.
```

Use a general-purpose sub-agent for the Quality track: it needs the skill tool to load stack-specific review skills.

### 4. Aggregate

Present the two reports under `## Spec` and `## Quality` headings, verbatim or lightly cleaned. Do **not** merge or rerank findings, because the tracks are deliberately separate (see _Why separate tracks_).

End with a one-line summary: total findings per track, and the worst issue _within each track_ (if any). Don't pick a single winner across tracks: that's the reranking the separation exists to prevent.

## Why separate tracks

A change can pass one track and fail another:

- Code that does exactly what the issue asked but falls over under real load, leaves risky paths untested, or carries lines nobody needs → **Spec pass, Quality fail.**
- Code that is solid engineering but implements the wrong thing → **Quality pass, Spec fail.**

Reporting them separately stops one track from masking the other.
