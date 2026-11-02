<!-- OPENWIKI:START -->

## OpenWiki

This repository has a generated `openwiki/` evidence index. It is optional just-in-time context, not required startup reading.

- Do not enumerate, preload, or search wikis at task start. Use retrieval when the user asks for it, when unfamiliar architecture or dependency behavior materially affects the task, or when source inspection leaves an important uncertainty. Stop once the question is grounded.
- When those conditions apply and OpenWiki retrieval tools are available, use `openwiki_search` for just-in-time context and `openwiki_read` for the relevant complete sections. If search returns `workspace_required`, ask which listed workspace to use and retry with its ID.
- Use `openwiki_list_workspaces` or `openwiki_list_wikis` when workspace membership itself needs to be discovered.
- If the retrieval tools are unavailable, read `openwiki/quickstart.md` and follow its links to the relevant pages.
- Treat source code and tests as authoritative. A brief's unknowns and review items are verification gaps, not automatic requirements.
- Prefer the narrowest quiet validation that proves the changed behavior. Preserve complete failure output.

The scheduled OpenWiki GitHub Actions workflow refreshes the repository wiki. Do not hand-edit generated OpenWiki pages unless explicitly asked; prefer updating source code/docs and letting OpenWiki regenerate.

<!-- OPENWIKI:END -->

## Naming

- Interfaces use the `I` prefix: `ISettings`, `IGitHubPersonalService`.
- Implementation classes drop the `I` prefix and use the interface name, optionally with a qualifier that describes the environment or strategy.
  - `ISettings` -> `LocalSettings` (Windows adapter), `InMemorySettings` (test fake)
  - `IGitHubPersonalService` -> `GitHubPersonalService`
- `I` is reserved for interfaces; never prefix a class with it.

## Testing

- For new tests, use NSubstitute when a test double is needed: `Substitute.For<IWhatever>()`. Add the `NSubstitute` package to `Directory.Packages.props` when the first test needs it.
- Do not add Moq or FakeItEasy; do not hand-roll new `Fake*`/`Stub*` classes for interfaces that NSubstitute can produce.
- Existing hand-written doubles (`InMemorySettings`, `StubHttpMessageHandler`, fake services) stay as they are; do not rewrite them just to switch libraries.
- Still use real platform objects where they are cheap and more faithful: a fake `HttpMessageHandler` for HTTP, and temp files/folders, temp git repos, or SQLite files instead of mocking the filesystem or git.

## Analyzers

- `TreatWarningsAsErrors` and `AnalysisLevel=latest-all` are on. Fix the diagnostic; do not silence it.
- A suppression must be member-scoped and carry the category, the rule id, and a `Justification` that explains why the analyzer is wrong *here*. Never an empty or copy-pasted justification.
- Before adding a `SuppressMessage`, check whether the same rule with the same wording already exists elsewhere. The third identical justification means the design is wrong — usually an ownership or API shape the analyzer cannot see (CA2000 on a handler passed to a constructor is the current example). Fix the cause with a helper, an owning wrapper, or a narrower signature instead.
- No blanket disables. `NoWarn` only per rule, with an inline comment saying why.
