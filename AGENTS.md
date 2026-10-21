# AGENTS.md

A Bun + TypeScript CLI (`sebastian`) plus the skills and templates it installs into other repos. `sebastian init` bootstraps a project for an issue-driven agent workflow.

## Layout

- `.oxlintrc.json` — Oxlint config. Extends the Effect `effecttsgo` strict preset and loads the local anti-slop plugin.
- `tools/oxlint/anti-slop/` — vendored anti-slop plugin. Generic rules are wired in the config; the Effect rules under `effect/` are available but off. Lint ignores this directory.

## Commands

- `bun install` — install dependencies.
- `bun run dev --help` — run the CLI from source.
- `bun run typecheck` — type-check with the Effect-aware `tsc` from `@effect/tsgo`.
- `bun run lint` — run Oxlint with type-aware rules.
- `bun run lint:fix` — apply Oxlint fixes.
- `bun test` — run the test suite.
- `bun run build` — compile `dist/sebastian.exe` (Windows) with the embedded `templates/` assets.

`prepare` runs `effect-tsgo patch --typescript --oxlint`, which patches the local TypeScript and Oxlint binaries. Re-run `bun install` after rebuilding `node_modules`.

## Verification

Run `bun run typecheck`, `bun run lint`, and `bun test` for CLI changes, then `bun run build`.

## Testing

Never smoke-test the built binary. Executing a compiled artifact makes an agent do unpredictable things, and the type checker, the linter, and the test suite already cover what a `--help` run would catch.
