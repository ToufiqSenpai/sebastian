# Files

- [System Overview and Runtime Topology](overview.md) - How Sebastian runs as one long-lived, single-repository ASP.NET Core process, mapping its two HTTP endpoints, two hosted services, singleton collaborators, and the intake → queue → git workspace → agent path with who owns each domain.
- [Startup, Composition, and Configuration Binding](startup-and-composition.md) - How Program.cs composes Sebastian — options binding with a source-generated validator, deliberate singleton factory registrations that share one instance with each hosted service, why credentials are constructed eagerly, how configuration keys map to SebastianConfig, and exactly what fails at startup versus later.
