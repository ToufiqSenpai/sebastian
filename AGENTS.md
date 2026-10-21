# AGENTS.md

A Bun + TypeScript CLI (`sebastian`) plus the skills and templates it installs into other repos. `sebastian init` bootstraps a project for an issue-driven agent workflow.

## Layout

- `src/` — the CLI. Entry point is `src/index.ts`; `commands/init.ts` owns the wizard, `files.ts` copies assets, `process.ts` runs child processes, `common/` holds logging and app config, and `libs/git.ts` / `libs/github-app.ts` / `libs/github-cli.ts` integrate with external tools.
- `src/**/*.test.ts` — Bun tests live beside the code they cover.
- `.oxlintrc.json` — Oxlint config. Extends the Effect `effecttsgo` strict preset and loads the local anti-slop plugin.
- `tools/oxlint/anti-slop/` — vendored anti-slop plugin. Generic rules are wired in the config; the Effect rules under `effect/` are available but off. Lint ignores this directory.
- `templates/skills/<name>/SKILL.md` — the product installed into target repos. One skill per directory; other skills reference each other by bare name.
- `templates/` — files copied into **target** repos (`AGENTS.md`, `docs/agents/`, and `skills/`). Not this repo's own rules; this repo has no `GLOSSARY.md` or `docs/adr/`.
- `docs/matt-skills.md` — Indonesian research notes on the upstream Matt Pocock skills; reference, not instructions.

`sebastian init` copies `templates/AGENTS.md`, `templates/docs/agents/`, and `templates/skills/` (to `.agents/skills/`), skipping files or whole skill directories that already exist; an existing `AGENTS.md` prompts whether to overwrite it. It appends `.agents/*` to `.gitignore`, initializes git, and creates a GitHub repository with `gh` when `origin` is missing.

## Commands

- `bun install` — install dependencies.
- `bun run dev --help` — run the CLI from source.
- `bun run typecheck` — type-check with the Effect-aware `tsc` from `@effect/tsgo`.
- `bun run lint` — run Oxlint with type-aware rules.
- `bun run lint:fix` — apply Oxlint fixes.
- `bun test` — run the test suite.
- `bun run build` — compile `dist/sebastian.exe` (Windows) with the embedded `templates/` assets.

`prepare` runs `effect-tsgo patch --typescript --oxlint`, which patches the local TypeScript and Oxlint binaries. Re-run `bun install` after rebuilding `node_modules`.

Effect v4 is pinned to `4.0.5`; `effect/cli` is still unstable, so upgrade `effect` and `@effect/platform-bun` together. `@effect/tsgo` and Oxlint are pinned to versions it supports (TypeScript `7.0.2`, Oxlint `1.88.0`).

## Workflow chain

`interview-me → planning → create-spec → implement-spec`, with `code-review` and `pr` invoked by `implement-spec` or directly. Tracker is always GitHub issues; the skills deliberately have no setup step and no local-file fallback.

- `disable-model-invocation: true` marks user-invoked skills. `code-review` and `pr` omit it on purpose so the model and `implement-spec` can invoke them.
- The "STOP YOUR TURN" endings in `interview-me` and `planning` are deliberate handoffs; don't soften them into automatic continuation.
- Skills must stay self-contained. Don't add a hard dependency on a skill outside this repo; copy the content and attribute it in the repo's central credits list, not per skill.

## Language

`docs/matt-skills.md` is Indonesian; `SKILL.md` files, `templates/`, CLI prompts/errors, and code comments are English. Keep the split.

## Verification

Run `bun run typecheck`, `bun run lint`, and `bun test` for CLI changes, then `bun run build`. Skill changes are verified by reading the `SKILL.md` end to end and checking that every skill it names still exists under `templates/skills/` and that the workflow chain above still holds.

Never smoke-test the built binary. Executing a compiled artifact makes an agent do unpredictable things, and the type checker, the linter, and the test suite already cover what a `--help` run would catch.
