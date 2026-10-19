## Domain Modeling

Actively build and sharpen the project's domain model while designing. Challenge, sharpen, and record as you go — never batch.

- Challenge any term that conflicts with the existing language in `GLOSSARY.md`; propose a precise canonical term when language is vague or overloaded.
- Stress-test domain relationships with concrete edge-case scenarios, forcing precision about where one concept ends and another begins.
- When a statement about how something works disagrees with the code, surface the contradiction and resolve which is right.
- Update `GLOSSARY.md` inline the moment a term is resolved. It holds language only: no implementation details, no specs, no scratch pad.
- Offer an ADR only when all three hold: hard to reverse, surprising without context, and the result of a real trade-off.

Layout: `GLOSSARY.md` at the repo root; ADRs at `docs/adr/NNNN-slug.md`. Create these lazily — only when the first term or decision needs a home.

Formats and full criteria: `docs/agents/glossary-format.md`, `docs/agents/adr-format.md`.
