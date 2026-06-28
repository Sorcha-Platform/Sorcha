# Tasks: Running the Tests README Section

**Input**: Design documents from `/specs/171-running-tests-docs/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅

**Organization**: Single user story (P1 only); no shared infrastructure required.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify the current state of `README.md` before editing.

- [ ] T001 Read `README.md` to identify the placement point adjacent to the `## Development` section

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: No shared infrastructure is required for a documentation-only change.

> This phase is intentionally empty — there are no blocking technical prerequisites for a Markdown edit.

**Checkpoint**: Foundation ready — user story implementation can begin immediately.

---

## Phase 3: User Story 1 — Discover how to run the tests from the README (Priority: P1) 🎯 MVP

**Goal**: Insert a clearly titled `## Running the tests` section into `README.md` containing `dotnet test` in copyable form, placed adjacent to the existing `## Development` section.

**Independent Test**: Open `README.md`, locate the "Running the tests" heading, confirm the `dotnet test` command appears in a fenced code block, and confirm no existing section is removed or altered.

### Implementation for User Story 1

- [ ] T002 [US1] Insert `## Running the tests` section in `README.md` immediately before or after the existing `## Development` section (research Decision 2), containing: a one-line description, a fenced `dotnet test` command block, a prerequisite note (.NET 10 SDK), and a pointer to `DEVELOPMENT.md` (research Decisions 1, 3, 4)

**Checkpoint**: User Story 1 is fully functional — `README.md` now contains the discoverable test-command section satisfying FR-001 through FR-007 and SC-001 through SC-004.

---

## Phase 4: Polish & Cross-Cutting Concerns

**Purpose**: Final review confirming the change is additive and accurate.

- [ ] T003 [P] [US1] Review rendered `README.md` to confirm: (a) "Running the tests" heading is scannable within 30 s (SC-001), (b) `dotnet test` command is accurate (SC-002 / FR-003), (c) no non-documentation file was modified (SC-003), (d) no pre-existing section was removed or semantically changed (SC-004 / FR-004)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Empty — no blocking work
- **User Story 1 (Phase 3)**: Depends on Setup (T001 read completes before T002 edit)
- **Polish (Phase 4)**: Depends on Phase 3 completion

### User Story Dependencies

- **User Story 1 (P1)**: Self-contained; no cross-story dependencies

### Within User Story 1

- T001 (read) → T002 (edit) → T003 (review)
- T003 can run in parallel with any other documentation review tasks if present

### Parallel Opportunities

- T003 is marked [P] — it can run in parallel with any post-implementation review task added later.

---

## Parallel Example: User Story 1

```bash
# Sequential — single file; no parallel opportunity within the story itself
T001: Read README.md to find placement point
T002: Insert "Running the tests" section
T003: Review rendered README.md for accuracy and completeness
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: read `README.md` (T001)
2. Complete Phase 3: insert section (T002)
3. **STOP and VALIDATE**: review rendered output (T003)
4. Ship — this is the entire feature

### Incremental Delivery

No further stories exist. The feature is complete after T003 passes.

---

## Notes

- [P] tasks = different files or no output dependency; safe to run concurrently
- [US1] label maps each task to User Story 1 for traceability
- Tests are not included — the feature specification does not request automated tests (docs-only change reviewed manually)
- No source code, configuration, build, or test behaviour is modified (FR-005, SC-003)
- Commit after T002 with reference to feature 171
