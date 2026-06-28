# Implementation Plan: Running the Tests README Section

**Branch**: `171-running-tests-docs` | **Date**: 2026-06-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/171-running-tests-docs/spec.md`

## Summary

Add a short, discoverable **"Running the tests"** section to the root `README.md` that surfaces the project's standard test command (`dotnet test`) in copyable form, placed near the existing `## Development` section so build/test guidance stays together. The change is documentation-only and additive — no existing README content is removed or semantically altered, and the deeper guide in `DEVELOPMENT.md` (already referenced) remains the canonical source for detailed test guidance.

Technical approach: a single Markdown edit to `README.md` inserting one new `##`-level section (heading + one-line description + fenced `dotnet test` command + a pointer to `DEVELOPMENT.md`). No code, config, or build changes.

## Technical Context

**Language/Version**: Markdown (GitHub-flavored) documentation; underlying project is .NET 10 / C# 14 (unchanged by this feature)

**Primary Dependencies**: None — documentation edit only. The documented command depends on the existing .NET 10 SDK toolchain already required to build the project.

**Storage**: N/A

**Testing**: Manual review of the rendered `README.md`; verification that the documented `dotnet test` command matches the project's actual suite runner (per CLAUDE.md and `DEVELOPMENT.md`)

**Target Platform**: Repository root `README.md` (rendered on GitHub and in local editors)

**Project Type**: Documentation change to an existing multi-service .NET solution

**Performance Goals**: A reader locates the test command in under 30 seconds of scanning (SC-001)

**Constraints**: Additive and documentation-only (FR-004, FR-005); short and focused (FR-006); consistent with existing README tone/formatting (FR-007); must not duplicate or contradict the existing `## Development` section

**Scale/Scope**: One section (~4–6 lines) in one file (`README.md`)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The Sorcha Constitution (v1.1.0) principles are evaluated for relevance to a documentation-only change:

| Principle | Applies? | Status |
|-----------|----------|--------|
| I. Microservices-First Architecture | No | No service/code change |
| II. Security First | No | No secrets, inputs, or sensitive data introduced |
| III. API Documentation | No | No API surface added or changed (this is repo-level prose, not XML/OpenAPI docs) |
| IV. Testing Requirements | Indirect (supports) | Documents the existing `dotnet test` flow; adds no code requiring coverage. The documented command itself must be accurate (FR-003) |
| V. Code Quality | No | No code; no compiler warnings possible |
| VI. Blueprint Creation Standards | No | N/A |
| VII. Domain-Driven Design | No | N/A |
| VIII. Observability by Default | No | N/A |

Documentation Sync Policy (CLAUDE.md): satisfied by design — this *is* the documentation update, and it stays consistent with `DEVELOPMENT.md` rather than duplicating it.

**Result**: PASS — no violations. No entries required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/171-running-tests-docs/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── spec.md              # Feature specification
└── checklists/          # Existing checklists for this feature
```

No `contracts/` directory is produced: this feature exposes no external interface (API, CLI command, schema, or grammar). It edits human-facing prose only, so a contract artifact would be empty by definition.

### Source Code (repository root)

```text
README.md                # The single file changed: insert "## Running the tests" near "## Development"
DEVELOPMENT.md           # Unchanged; remains the canonical deep test/build guide referenced by README
```

**Structure Decision**: No source tree is created or modified. The feature is a localized, additive edit to the existing root `README.md`. The new section is placed adjacent to the existing `## Development` section (currently the last content section before `## License`/`## Links`) so build and test information stay co-located, satisfying FR-007 and the spec's placement assumption.

## Complexity Tracking

> No constitution violations — section intentionally left empty.
