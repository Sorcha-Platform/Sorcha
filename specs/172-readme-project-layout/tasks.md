---
description: "Task list for Feature 172 – README Project Layout Section"
---

# Tasks: README Project Layout Section

**Input**: Design documents from `/specs/172-readme-project-layout/`

**Prerequisites**: plan.md ✅, spec.md ✅, quickstart.md ✅, data-model.md ✅

**Tests**: No automated tests — validation is a content-accuracy check (manual + shell one-liners from quickstart.md).

**Organization**: Single user story (US1). All tasks are sequential; the feature is a one-file Markdown edit.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel
- **[US1]**: User Story 1 – Newcomer orients to the repository

---

## Phase 1: Setup

**Purpose**: Confirm the insertion point in the existing README and verify all 14 listed directories exist.

- [X] T001 Verify existing README.md heading flow and confirm `## Documentation` section is the correct insertion point (`README.md`)
- [X] T002 [P] Confirm all 14 top-level directories (`bench`, `blueprints`, `demos`, `docker`, `docs`, `infra`, `mobile`, `ops`, `samples`, `scripts`, `specs`, `src`, `tests`, `walkthroughs`) exist at repository root

**Checkpoint**: Insertion point confirmed, directories verified present — implementation can begin.

---

## Phase 2: User Story 1 – Newcomer orients to the repository (Priority: P1) 🎯 MVP

**Goal**: Add a `## Project layout` section to `README.md` that lists each top-level directory with a one-line purpose and a pointer to the fuller reference doc. Delivers FR-001 through FR-007 and SC-001 through SC-004.

**Independent Test**: Run all five validation scenarios from `specs/172-readme-project-layout/quickstart.md`.

- [X] T003 [US1] Inspect each of the 14 root directories to confirm descriptions are accurate before writing (`bench/`, `blueprints/`, `demos/`, `docker/`, `docs/`, `infra/`, `mobile/`, `ops/`, `samples/`, `scripts/`, `specs/`, `src/`, `tests/`, `walkthroughs/`)
- [X] T004 [US1] Insert `## Project layout` section into `README.md` immediately before `## Documentation`, listing all 14 directories with one-line descriptions and a link to `docs/reference/project-structure.md` (FR-001–007)

---

## Phase 3: Polish & Validation

**Purpose**: Confirm the edit is additive-only and every acceptance criterion is met.

- [X] T005 Run quickstart.md Scenario A — confirm exactly one `## Project layout` heading exists (`README.md`)
- [X] T006 Run quickstart.md Scenario B — confirm listed directories match repo root exactly (no omissions, no phantoms)
- [X] T007 Run quickstart.md Scenario C — confirm `git diff master...HEAD` touches only `README.md` and `specs/172-*` files and adds no removed lines to README
- [X] T008 Run quickstart.md Scenario D — confirm `docs/reference/project-structure.md` is referenced within the new section
- [X] T009 Run quickstart.md Scenario E (manual) — confirm no entry references a nested path

---

## Dependencies

```
T001 → T004
T002 → T003 → T004
T004 → T005 → T006 → T007 → T008 → T009
```

T001 and T002 can run in parallel (both are read-only checks).
T003 (content authoring) depends on T002 (directories verified).
All validation tasks (T005–T009) depend on T004 (section inserted).

## Parallel Execution

| Parallel group | Tasks | Notes |
|----------------|-------|-------|
| Group A | T001, T002 | Both read-only; no dependencies |
| Sequential | T003 → T004 | Content authoring then insertion |
| Sequential | T005 → T006 → T007 → T008 → T009 | Validation in order |

## Implementation Strategy

**MVP = US1** (the only story). The entire feature is delivered by T004. Validation tasks T005–T009 confirm correctness but do not change any file. Suggested order:

1. Run T001 + T002 in parallel to confirm the README structure and directory set.
2. T003: Draft the 14 directory descriptions while inspecting each folder.
3. T004: Insert the section at the confirmed location.
4. T005–T009: Run the validation one-liners from quickstart.md.
