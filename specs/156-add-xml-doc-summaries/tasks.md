---
description: "Task list for feature 156 — Add Missing XML Doc Summaries to One Public C# Class"
---

# Tasks: Add Missing XML Doc Summaries to One Public C# Class

**Input**: Design documents from `/specs/156-add-xml-doc-summaries/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, quickstart.md ✅, data-model.md ✅

**Tests**: No new tests required — this is a documentation-only change; existing tests must continue to pass (FR-005, plan.md).

**Organization**: Single user story (US1) — fully sequential within the story since each step depends on the previous.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Exact file paths included in descriptions

---

## Phase 1: Setup (Candidate Discovery)

**Purpose**: Run the doc-coverage scan to confirm the recommended candidate and enumerate undocumented members.

**Why this is foundational**: All subsequent work targets exactly one file, and that file must be confirmed before any edits begin (FR-001).

- [X] T001 Confirm the recommended candidate by reading `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs` and listing every public member that currently lacks a `/// <summary>` immediately above it (the "before" set)
- [X] T002 Confirm the class implements `ICredentialApiService` and identify which members directly implement a documented interface member (these are `<inheritdoc/>` candidates per research.md Decision 3)

**Checkpoint**: Candidate confirmed, "before" set recorded — implementation can begin

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Read the implemented interface to understand which members have upstream XML docs that `<inheritdoc/>` can inherit from.

**⚠️ CRITICAL**: Must know the interface's documented surface before writing any summaries, to avoid writing prose where `<inheritdoc/>` is more accurate.

- [X] T003 Read `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/ICredentialApiService.cs` (or equivalent interface file) and record which method/property signatures carry existing `/// <summary>` docs

**Checkpoint**: Interface documentation surface mapped — US1 implementation can begin

---

## Phase 3: User Story 1 — Document the Undocumented Public Surface (Priority: P1) 🎯 MVP

**Goal**: Add `/// <summary>` (or `/// <inheritdoc/>`) to every undocumented public member of `CredentialApiService.cs` so the class's public API is fully self-describing per the project convention.

**Independent Test**: After edits, re-scan `CredentialApiService.cs` — zero public members should be without a `/// <summary>` or `/// <inheritdoc/>`. The diff must contain only added `///` lines; no executable code lines change.

**Acceptance Scenarios** (from spec.md US1):
1. Every public member (and the type declaration) has an accurate `/// <summary>` or `/// <inheritdoc/>` after the change.
2. `dotnet build` on the owning project produces no new warnings.
3. `git diff --stat` shows exactly one `.cs` file changed.

### Implementation for User Story 1

- [X] T004 [US1] Add `/// <summary>` to the `CredentialApiService` class declaration if it lacks one — one accurate sentence describing the service's responsibility — in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs`
- [X] T005 [US1] For each public method/property that directly implements a documented member of `ICredentialApiService`, add `/// <inheritdoc/>` immediately above the member in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs`
- [X] T006 [US1] For each remaining undocumented public method/property (those not covered by `<inheritdoc/>`), add a concise, accurate `/// <summary> ... </summary>` block above the member in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs` — summaries must describe observable API-level intent without restating the member name verbatim (FR-003)
- [X] T007 [US1] Review all added summaries against the member signatures to verify each is accurate, non-misleading, and free of unverifiable implementation claims (SC-005, FR-003)

**Checkpoint**: At this point, User Story 1 is complete — every public member has documentation; the file is fully self-describing

---

## Phase 4: Polish & Verification

**Purpose**: Mechanically verify all success criteria (SC-001 through SC-005) and confirm no regressions.

**Verification map** (from quickstart.md Step 4):

| Check | Command | Expected | Criterion |
|-------|---------|----------|-----------|
| Exactly one file changed | `git diff --stat` | One `.cs` file listed | SC-003 |
| Only comment lines added | `git diff` | All added lines are `///`; no removed/changed executable lines | SC-004 |
| 100% public members documented | Re-scan `CredentialApiService.cs` | Zero undocumented public members remain | SC-001 |
| Build stays clean | `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/` | Succeeds, no new warnings | SC-002 |
| Summaries accurate | Manual diff review | Each summary correct, no verbatim-name restatements | SC-005 |

- [X] T008 Run `git diff --stat` and confirm exactly one `.cs` file appears in the output (SC-003)
- [X] T009 Run `git diff` and confirm every added line begins with `///` and no executable code lines were removed or changed (SC-004)
- [X] T010 Rescan `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs` for undocumented public members and confirm the result is empty (SC-001)
- [X] T011 Run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/` and confirm it succeeds with no new warnings (SC-002, Constitution Principle V)

---

## Dependencies

```
T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011
```

All tasks are sequential: candidate confirmation gates interface reading, which gates writing accurate summaries; verification gates on the edits being complete.

## Implementation Strategy

**MVP Scope = Phase 3 (US1 only)** — this is the entire deliverable. The feature is a single cohesive action: one file, one class, all public members documented. Phases 1–2 are lightweight pre-reads (not code changes); Phase 4 is the verification pass.

**Recommended execution order**: Read → Map → Write (class decl) → Write (inheritdoc members) → Write (remaining summaries) → Review → Verify.

**Parallel opportunities**: None — each phase depends on the previous. T005 and T006 could be ordered either way within Phase 3, but are not independent of T004.

**Estimated effort**: Low. ~60 `/// <inheritdoc/>` or `/// <summary>` additions to a single file; no code changes; no new test authoring.
