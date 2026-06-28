# Phase 0 Research: Running the Tests README Section

All Technical Context items are known; no `NEEDS CLARIFICATION` markers remained. The research below records the decisions that ground the (trivial) implementation.

## Decision 1 — Documented test command

- **Decision**: Document `dotnet test` (run from the repository root) as the full-suite command.
- **Rationale**: `CLAUDE.md` ("Build & Test") and the project's standard workflow use `dotnet test` to run all tests across the 50+ test projects. This is the project's actual suite runner, satisfying FR-003 and SC-002. Optionally mention `dotnet restore && dotnet build` as the preceding steps already shown in CLAUDE.md's Quick Start, but keep the section short (FR-006).
- **Alternatives considered**:
  - A per-project filtered command (`dotnet test --filter ...`) — rejected: that is detail for `DEVELOPMENT.md`, not the "run the whole suite" entry point.
  - A coverage variant (`dotnet test --collect:"XPlat Code Coverage"`) — rejected for the short section; belongs in the deeper guide.

## Decision 2 — Placement within README

- **Decision**: Insert a new `## Running the tests` section immediately adjacent to the existing `## Development` section (README.md line ~276).
- **Rationale**: FR-007 and the spec's placement assumption want test info next to build/development info. The `## Development` section already points to `DEVELOPMENT.md`; placing the quick command directly before/after it gives readers the fast path up front while preserving the deep-link.
- **Alternatives considered**:
  - Adding it under `## Quick Start` — rejected: Quick Start is Docker-run oriented (running the platform), not building/testing from source.
  - Replacing the `## Development` pointer — rejected: violates FR-004 (must be additive, not alter existing content).

## Decision 3 — Relationship to existing content

- **Decision**: The new section complements, not duplicates, the `## Development` section. It contains a one-line description, the fenced command, and a pointer to `DEVELOPMENT.md` for deeper detail.
- **Rationale**: Edge cases in the spec require no duplication/contradiction with the existing `DEVELOPMENT.md` pointer (FR-004, SC-004). Keeping `DEVELOPMENT.md` as the canonical deep guide avoids documentation drift.
- **Alternatives considered**: Reproducing prerequisites inline — rejected; instead reference the existing Prerequisites/Development guidance so there is a single source of truth.

## Decision 4 — Prerequisite handling

- **Decision**: Note that the .NET 10 SDK is required (or point to the README's existing Prerequisites / `DEVELOPMENT.md`) so a missing toolchain produces an understandable failure rather than a mysterious one.
- **Rationale**: Spec edge case — "if prerequisites are not installed, the section should make the dependency clear or point to where prerequisites are listed."
- **Alternatives considered**: Silent omission — rejected; fails the edge-case expectation.

**Output**: No open questions. Ready for Phase 1.
