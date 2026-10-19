# AGENTS.md

A Bun + TypeScript CLI (`sebastian`) plus the skills and templates it installs into other repos. `sebastian init` bootstraps a project for an issue-driven agent workflow.

## Layout

- `src/` — the CLI. Entry point is `src/index.ts`; `commands/init.ts` owns the wizard, `files.ts` copies assets, `process.ts` runs child processes, and `libs/git.ts` / `libs/github.ts` integrate with external tools.
- `test/` — Bun tests for asset copying, git helpers, GitHub argument building, and the CLI surface.
- `templates/skills/<name>/SKILL.md` — the product installed into target repos. One skill per directory; other skills reference each other by bare name.
- `templates/` — files copied into **target** repos (`AGENTS.md`, glossary/ADR formats, and `skills/`). Not this repo's own rules; this repo has no `GLOSSARY.md` or `docs/adr/`.
- `docs/matt-skills.md` — Indonesian research notes on the upstream Matt Pocock skills; reference, not instructions.

`sebastian init` copies `templates/AGENTS.md`, `templates/docs/agents/`, and `templates/skills/` (to `.agents/skills/`), skipping files or whole skill directories that already exist. It initializes git, optionally commits, and creates a GitHub repository with `gh` when `origin` is missing.

## Commands

- `bun install` — install dependencies.
- `bun run dev --help` — run the CLI from source.
- `bun run typecheck` — type-check with `tsc`.
- `bun test` — run the test suite.
- `bun run build` — compile `dist/sebastian.exe` (Windows) with the embedded `templates/` assets.

Effect v4 is pinned to `4.0.5`; `effect/cli` is still unstable, so upgrade `effect` and `@effect/platform-bun` together.

## Workflow chain

`interview-me → planning → create-spec → implement-spec`, with `code-review` and `pr` invoked by `implement-spec` or directly. Tracker is always GitHub issues; the skills deliberately have no setup step and no local-file fallback.

- `disable-model-invocation: true` marks user-invoked skills. `code-review` and `pr` omit it on purpose so the model and `implement-spec` can invoke them.
- The "STOP YOUR TURN" endings in `interview-me` and `planning` are deliberate handoffs; don't soften them into automatic continuation.
- Skills must stay self-contained. Don't add a hard dependency on a skill outside this repo; copy the content and attribute it in `CREDITS.md` (see `templates/skills/pr/CREDITS.md`).

## Language

`docs/matt-skills.md` is Indonesian; `SKILL.md` files, `templates/`, CLI prompts/errors, and code comments are English. Keep the split.

## Verification

Run `bun run check` and `bun test` for CLI changes, then `bun run build` and smoke-test `dist/sebastian.exe --help`. Skill changes are verified by reading the `SKILL.md` end to end and checking that every skill it names still exists under `templates/skills/` and that the workflow chain above still holds.
