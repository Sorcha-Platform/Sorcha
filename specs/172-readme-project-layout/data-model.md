# Phase 1 Data Model: README Project Layout Section

This feature is documentation, so the "data model" is the content schema of the new section rather than any runtime entity.

## Entity: Project layout section

A single README subsection.

| Field | Type | Rules |
|-------|------|-------|
| Heading | string | MUST be `## Project layout` (level-2, matching sibling sections). FR-001. |
| Intro line | string (optional) | One short sentence; SHOULD reference `docs/reference/project-structure.md` for the fuller tree. FR-005. |
| Entries | list of *Directory entry* | 14 entries, one per top-level directory. FR-002. |

## Entity: Directory entry

One row/item pairing a directory with its purpose.

| Field | Type | Rules |
|-------|------|-------|
| Name | string | A directory name that MUST exist at the repository root at authoring time. Rendered as `` `name/` ``. FR-002, FR-006, SC-002. |
| Purpose | string | A single-line description accurately reflecting the directory's contents. No nested-path detail. FR-003, FR-006, Edge: depth creep. |

## Content (authoring-time values)

The 14 entries, with verified purposes (descriptions confirmed against directory contents during planning):

| Directory | Purpose |
|-----------|---------|
| `bench/` | Performance benchmarks and recorded baselines (e.g. Validator throughput). |
| `blueprints/` | Blueprint JSON templates, schemas, and worked examples. |
| `demos/` | Self-contained demo scenarios (Assured Identity, Membership). |
| `docker/` | Docker Compose configs, environment appsettings, and Caddy/gateway setup. |
| `docs/` | Project documentation — guides, getting-started, and reference material. |
| `infra/` | Infrastructure-as-code (Bicep) and deployment scripts. |
| `mobile/` | Mobile app sources and build scripts (Wallet PWA / Capacitor). |
| `ops/` | Operational assets such as Grafana dashboards. |
| `samples/` | Sample applications and reference portals. |
| `scripts/` | Build, bootstrap, CI, and maintenance scripts. |
| `specs/` | Spec Kit feature specifications, plans, and tasks. |
| `src/` | Application source — services, apps, and shared libraries. |
| `tests/` | Test projects across the solution. |
| `walkthroughs/` | Interactive, runnable demos and test scripts. |

> Wording may be polished during implementation; the **invariant** is that every name exists, every top-level directory is present, and each purpose is accurate (FR-006, SC-002).

## Invariants (validation rules)

- **INV-1** — Completeness: the set of `Name` values equals the set of top-level directories at the repo root (no omissions, no phantoms). SC-002.
- **INV-2** — Existence: every `Name` resolves to an existing root directory. Acceptance Scenario 2.
- **INV-3** — Additive: no pre-existing README byte outside the inserted block changes. FR-004, SC-004.
- **INV-4** — Single level: no entry documents a nested subdirectory. Edge: depth creep.

## State transitions

None — static content.
