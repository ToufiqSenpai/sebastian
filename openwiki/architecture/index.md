# Files

- [System Overview and Runtime Topology](overview.md) - How Sebastian runs as one long-lived, single-repository ASP.NET Core process, mapping its two HTTP endpoints, two hosted services, singleton collaborators, and the intake → queue → git workspace → agent path with who owns each domain.
- [Startup, Composition, and Configuration Binding](startup-and-composition.md) - How Program.cs composes Sebastian — {env:NAME} placeholder expansion before options binding, options binding with a source-generated validator, the committed appsettings.json template, deliberate singleton factory registrations that share one instance with each hosted service, the eager resolutions that fail startup, and exactly what fails at boot versus on the first job.
