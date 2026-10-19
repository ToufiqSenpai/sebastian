---
name: planning
description: Turn an understood ask into a validated, codebase-grounded technical plan, then hand off to direct implementation or create-spec.
disable-model-invocation: true
---

# Planning

Take a clarified ask, explore the codebase, validate only the decisions the exploration surfaced, and present the final plan. Then the user chooses the path: build it directly (work that fits one session) or publish a spec with `create-spec` (work that doesn't).

This skill writes nothing. The plan lives in the conversation; artifacts are `create-spec`'s job.

## Critical Constraints

These override everything below.

- Do NOT write or edit code during planning.
- Do NOT implement the feature — not the core of it, not a piece of it, not a prototype "to check".
- Do NOT start the work: no implementation subagents, no branches, no build or run commands aimed at the feature.
- Exploration is expected (reading, grepping, fetching docs). It stops at understanding and decisions.
- Plan approval is not a go signal. After presenting the plan, STOP and wait. Work begins only when the user tells you to proceed; if they want a spec instead, they invoke `create-spec`.

## Process

### 1. Decide the exploration

Unless this conversation already holds the facts from an earlier exploration, the codebase must be explored now.

- **Inline** when the change is small and the area known: one to three files, read them directly.
- **Subagent** when the area is unknown, the change crosses packages or layers, many call sites need tracing, or the codebase is large. Exploration is token-heavy; a subagent keeps it out of this conversation and returns a compact report.
- **Skip** when the conversation already holds the facts.

Keep the project's domain glossary vocabulary, and respect ADRs in the area being touched.

The subagent brief is question-driven, never "explore the repo": pass the stated intent plus the specific questions this plan needs answered — where X lives, which pattern similar features follow, where the tests for this seam live, what breaks if X changes — and ask for one dedicated section: **findings that contradict the intent**. Facts, not opinions.

### 2. Validation gate — decisions, not facts

Facts exploration gathered (files, dependencies, signatures) go straight into the plan; never ask the user to confirm them. Validate only what changes the plan. Usually zero to three items:

- **Seams** — where the change will be tested and where it integrates. A wrong seam means brittle tests and an expensive refactor. Prefer existing seams over new ones, at the highest point possible, and confirm them with the user.
- **Findings that change direction**, split by kind:
  - changes *what/why* (the feature already partly exists, a hard constraint makes the ask impossible) → raise it with the user as a product question; do not decide it here;
  - changes *how* (mandatory prefactoring, a larger blast radius, other consumers affected) → validate here: in scope now, or out?
- **Approach forks** — only when exploration genuinely found two or more viable approaches with real trade-offs. Present each with a recommendation. When only one approach is sensible, do not ask.

Present as a short numbered list of choices, each with a recommendation and what hangs on it. These are technical decisions, so answer-first beats open questioning. If nothing changes the plan, skip the gate.

### 3. Present the final plan

Deliver the plan in the conversation, tight:

- **Intent** — one line, from the confirmed statement.
- **Approach** — what changes, and why this way.
- **Seams** — what gets tested where.
- **Affected areas** — files and packages the change touches or creates, grounded in exploration.
- **Changes** — the concrete contract: new or changed classes, functions, methods, constants, and types, each with its name, signature, and location; API endpoints with their request/response shapes; schema changes and migrations; existing helpers or modules to reuse. Contract level, not function bodies.
- **Scope** — in now / explicitly out, including prefactoring.
- **Open risks** — anything unresolved.

Every item in the list comes from exploration; anything the plan needs but exploration did not surface goes under Open risks, not invented on the spot. Library and framework APIs are verified against official documentation for the detected version — see `references/verify-library-apis.md`.

Fold in corrections and restate until the user has nothing left to correct.

### 4. Terminal Turn & Stop Rule (CRITICAL)

When the plan is final, give the user the choice — with your recommendation:

1. **Direct implementation** — when the work fits one session / one context window. The plan is already in this conversation; next turn it is simply built, in this session.
2. **`create-spec`** — when it doesn't fit one session. It synthesizes this conversation into a spec plus tracer-bullet tickets and publishes them as GitHub issues. It does not interview, so run it in this same session, without clearing or compacting the context.

Then **STOP YOUR TURN IMMEDIATELY**. Presenting the plan and asking this question is the last thing this skill does. Do NOT invoke tools, start the implementation, or write the spec yourself. Approval is not a go signal — wait for the user to say to proceed or to invoke `create-spec`.
