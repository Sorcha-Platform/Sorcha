# Phase 0 Research: README Project Layout Section

The spec is unambiguous (a docs-only, additive README section) and the Technical Context carries **no `NEEDS CLARIFICATION` markers**. Research here records the few decisions that shape the edit.

## Decision 1 — Section placement within the README

- **Decision**: Insert `## Project layout` immediately **before** the existing `## Documentation` section (i.e. after `## For AI Agents and Integrators`).
- **Rationale**: The README's existing flow is overview → quick start → how-it-works → CLI → architecture → configuration → AI integrators → documentation → walkthroughs → development. A directory map is orientation material; placing it next to the `## Documentation` links (which already point into `docs/`) groups it with the "where do I look next" content and sits naturally beside the link out to `docs/reference/project-structure.md`. It does not interrupt the getting-started narrative.
- **Alternatives considered**:
  - *After `## Architecture Overview`* — plausible, but separates the map from the documentation links and pushes it above Configuration, breaking the setup narrative.
  - *At the very end before `## Links`* — too easy to miss; defeats the "at a glance for newcomers" goal (SC-001).

## Decision 2 — Format of the directory list

- **Decision**: A Markdown table (`| Directory | Purpose |`) or a tight bullet list of `` `dir/` `` — one line per directory. Use the format that matches the surrounding README tables for visual consistency (the README already uses tables for Documentation and Walkthroughs).
- **Rationale**: A table reads as a scannable two-column map and matches the established README style, supporting the <30s comprehension target (SC-001).
- **Alternatives considered**: A code-fenced tree (like `docs/reference/project-structure.md`) — rejected as it invites depth-creep and duplicates the fuller reference (Edge Cases: depth creep, duplication).

## Decision 3 — Authoritative directory set

- **Decision**: List exactly the 14 directories present at the repository root at authoring time, verified by `ls -d */`:
  `bench`, `blueprints`, `demos`, `docker`, `docs`, `infra`, `mobile`, `ops`, `samples`, `scripts`, `specs`, `src`, `tests`, `walkthroughs`.
- **Rationale**: FR-002 / SC-002 require 100% coverage and zero phantom entries. The set was confirmed against the live tree during planning.
- **Alternatives considered**: Hard-coding from memory or from `project-structure.md` — rejected; the live tree is the source of truth and the two could drift.

## Decision 4 — No `contracts/` artifact

- **Decision**: Skip the `contracts/` directory.
- **Rationale**: The feature exposes no programmatic interface — it is prose in a Markdown file. The Phase 1 contract step explicitly permits skipping for purely internal/documentation changes.

## Decision 5 — Maintenance / drift posture

- **Decision**: The section is maintained manually; no automated sync. The quickstart provides a one-liner to detect drift on demand.
- **Rationale**: Matches the spec's Assumptions ("maintained manually thereafter; automated synchronisation is out of scope") and the Edge Case on directory-set drift.

## Open questions

None. All Technical Context fields are resolved; ready for Phase 1.
