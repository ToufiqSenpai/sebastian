# Files

- [Testing Strategy and Harness](test-strategy.md) - How Sebastian's single xunit project is wired (InternalsVisibleTo, a deliberately less strict test csproj), the test-only seams that make internal services assertable (TestHostEnvironment, TestRepository's bare-origin git fixture, Options.Create config builders, direct calls to PrepareWorkspaceAsync and AgentRole.CreateOptions), which invariant each test file pins, and the large areas — webhook intake, GitHubService, agent runs, mirror sync, shutdown — that no test covers.
