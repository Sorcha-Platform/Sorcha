# Implementation Plan: README Project Layout Section

**Branch**: `172-readme-project-layout` | **Date**: 2026-06-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/172-readme-project-layout/spec.md`

## Summary

Add a short, additive **"Project layout"** section to the repository root `README.md` that lists the 14 top-level directories (`bench`, `blueprints`, `demos`, `docker`, `docs`, `infra`, `mobile`, `ops`, `samples`, `scripts`, `specs`, `src`, `tests`, `walkthroughs`) with a one-line purpose each, and points readers to the fuller `docs/reference/project-structure.md`. The change is documentation-only: no source, test, or configuration files are touched, and all existing README content is preserved.

**Technical approach**: A single Markdown edit to `README.md`. The new section is inserted into the existing heading flow (immediately before the `## Documentation` section, grouping it with the other orientation material). Directory names and descriptions are verified against the live repository tree at authoring time. Validation is a manual/scripted check that each listed directory exists at the root and that no top-level directory is omitted.

## Technical Context

**Language/Version**: Markdown (GitHub-Flavored) — no code

**Primary Dependencies**: None

**Storage**: N/A

**Testing**: Manual review + a shell one-liner that diffs the listed directory names against `ls -d */` at the repo root (see quickstart.md)

**Target Platform**: Repository `README.md` rendered on GitHub and in editors

**Project Type**: Documentation change (single-file edit)

**Performance Goals**: N/A (reader can identify a directory's purpose in <30s — SC-001)

**Constraints**: Additive only; one-level map only (no nested subdirectories); must not duplicate `docs/reference/project-structure.md`

**Scale/Scope**: One README section, 14 directory entries, single file modified

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The Sorcha constitution governs service code, security, API docs, and testing. This feature is a **documentation-only README edit** and engages no code-bearing principles. Assessment of each principle:

| Principle | Applicability | Status |
|-----------|---------------|--------|
| I. Microservices-First | No services touched | ✅ N/A |
| II. Security First | No code, no secrets, no inputs | ✅ N/A |
| III. API Documentation | No APIs added/changed | ✅ N/A |
| IV. Testing Requirements | No code under test; validation is a content-accuracy check | ✅ N/A |
| V. Code Quality | No code changed; no build impact | ✅ N/A |
| VI. Blueprint Standards | No blueprints touched | ✅ N/A |
| VII. Domain-Driven Design | Section uses ubiquitous-language terms where relevant | ✅ Pass |
| VIII. Observability | No runtime behaviour | ✅ N/A |

The Documentation Sync Policy (CLAUDE.md) is directly *served* by this feature rather than challenged by it. **No violations. No complexity to justify.**

## Project Structure

### Documentation (this feature)

```text
specs/172-readme-project-layout/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # Feature specification (already present)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
└── checklists/          # Pre-existing
```

No `contracts/` directory is generated — this feature exposes no external interface (it is a prose/Markdown change). See research.md for the rationale.

### Source Code (repository root)

This feature modifies exactly one file. No source tree changes.

```text
README.md                # <-- the only file edited: insert "## Project layout" section
docs/reference/project-structure.md   # referenced (not modified) as the fuller source tree
```

The directories the new section *documents* (verified present at authoring time):

```text
bench/         blueprints/    demos/         docker/
docs/          infra/         mobile/        ops/
samples/       scripts/       specs/         src/
tests/         walkthroughs/
```

**Structure Decision**: Single-file documentation edit. The "Project layout" section is inserted into `README.md` immediately before the existing `## Documentation` heading (after `## Configuration` / `## For AI Agents and Integrators`), keeping it within the natural orientation flow and adjacent to the link out to `docs/reference/project-structure.md`. No new directories, projects, or build wiring are introduced.

## Complexity Tracking

> No Constitution Check violations — section intentionally left empty.
