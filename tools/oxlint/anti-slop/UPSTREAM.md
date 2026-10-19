# Anti-slop provenance

- Source: bundled `install-anti-slop` skill assets at `.agents/skills/install-anti-slop/assets/anti-slop`.
- Upstream revision: unknown; the bundled asset does not include a source repository commit.
- Installed entry point: `tools/oxlint/anti-slop/index.ts`.
- Vendored rule assets: `tools/oxlint/anti-slop/` including the nested Stylistic license and provenance.
- Runtime dependency: `@oxlint/plugins@1.85.0`, pinned to the repository's `oxlint@1.85.0` version.
- Type-aware linting dependency: `oxlint-tsgolint@7.0.2002`, required by the merged reference configuration.
- Local deviations: root Oxlint configuration merges the reference rules from `ToufiqSenpai/bun-boilerplate` (main branch, exact commit unknown) with the vendored anti-slop plugin and local ignores.
