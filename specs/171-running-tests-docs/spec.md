# Feature Specification: Running the Tests README Section

**Feature Branch**: `171-running-tests-docs`

**Created**: 2026-06-28

**Status**: Draft

**Input**: User description: "Add a short 'Running the tests' section to the repo README documenting the test command; docs-only, additive."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover how to run the tests from the README (Priority: P1)

A developer or evaluator who has just cloned (or is browsing) the repository opens the root `README.md` to understand the project. They want to run the test suite to confirm their environment is healthy, but the README currently only points them to a separate `DEVELOPMENT.md` file under a general "Development" heading. They scan the README and find a clearly titled "Running the tests" section that gives them the exact command to run the tests, so they can verify the build without leaving the README or hunting through other docs.

**Why this priority**: This is the entire feature. Without a discoverable, correctly named section containing the test command, the change delivers no value. It is independently testable and shippable on its own.

**Independent Test**: Open the root `README.md`, locate a "Running the tests" section, and confirm it contains the documented test command. The section can be reviewed and validated in isolation by reading the rendered README.

**Acceptance Scenarios**:

1. **Given** the root `README.md`, **When** a reader scans its headings, **Then** a section titled "Running the tests" is present.
2. **Given** the "Running the tests" section, **When** a reader follows it, **Then** the documented command to run the full test suite is shown in a copyable form.
3. **Given** a developer with the documented prerequisites met, **When** they copy and run the command from the section verbatim, **Then** the project's tests execute.

---

### Edge Cases

- The new section must not duplicate or contradict the existing "Development" section that points to `DEVELOPMENT.md`; it complements it (a quick command up front, with the deeper guide still referenced).
- The documented command must match the command the project actually uses, so a reader copying it does not hit an error.
- If a reader's prerequisites (e.g. SDK/toolchain) are not installed, the section should make the dependency clear or point to where prerequisites are listed, so the failure is understandable rather than mysterious.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The root `README.md` MUST contain a clearly identifiable section titled "Running the tests".
- **FR-002**: The section MUST document the command used to run the project's full test suite, presented in a copyable (code-formatted) form.
- **FR-003**: The documented command MUST be accurate — i.e. it MUST be the command the project actually uses to run its tests.
- **FR-004**: The change MUST be additive — it MUST NOT remove or alter existing README content beyond inserting the new section (and any minimal surrounding navigation needed to place it).
- **FR-005**: The change MUST be documentation-only — no source code, configuration, build, or test behavior is modified.
- **FR-006**: The section MUST remain short and focused on the test command, deferring deeper detail to the existing development guide rather than reproducing it.
- **FR-007**: The section SHOULD be placed where a reader naturally looks for it (near build/development information) and SHOULD remain consistent with the README's existing tone and formatting.

### Key Entities

- **README "Running the tests" section**: A new, short documentation block in the root `README.md` whose purpose is to surface the test command; attributes are its heading/title, the documented command, and an optional brief pointer to the fuller development guide.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reader can locate the test command in the README in under 30 seconds of scanning, without opening any other file.
- **SC-002**: 100% of the test commands shown in the section run successfully (no typos or stale commands) when prerequisites are met.
- **SC-003**: The change touches documentation only — zero non-documentation files are modified.
- **SC-004**: Existing README content is preserved — no pre-existing section is removed or semantically changed by the addition.

## Assumptions

- The "repo README" refers to the root `README.md` at the repository top level.
- The documented test command is the project's standard suite-runner command (`dotnet test`), consistent with the existing build/test guidance; deeper test guidance continues to live in `DEVELOPMENT.md`, which the README already references.
- "Short" means a few lines — a heading, a one-line description, and the command (optionally with a pointer to `DEVELOPMENT.md`) — not a full testing guide.
- The section is placed near the existing "Development" content so build and test information stay together.
- No translations or localized README variants are in scope.
