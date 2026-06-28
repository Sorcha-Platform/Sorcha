# Feature Specification: README Project Layout Section

**Feature Branch**: `172-readme-project-layout`

**Created**: 2026-06-28

**Status**: Draft

**Input**: User description: "Add a short 'Project layout' section to the repo README briefly listing the top-level directories and their purpose; docs-only, additive."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Newcomer orients to the repository (Priority: P1)

A developer or contributor opening the repository for the first time reads the README and wants to know, at a glance, what each top-level directory contains so they can find where to make a change without grepping the whole tree or asking someone.

**Why this priority**: This is the entire value of the feature. Without it there is nothing to deliver. A concise directory map is the single most useful orientation aid for a newcomer and directly reduces time-to-first-contribution.

**Independent Test**: Open the README, locate the "Project layout" section, and confirm each listed directory exists in the repository root and its one-line description matches its actual contents. This is fully testable on its own and delivers the complete value of the feature.

**Acceptance Scenarios**:

1. **Given** a reader is on the repository README, **When** they scan the document, **Then** they find a clearly headed "Project layout" section listing the top-level directories.
2. **Given** the "Project layout" section, **When** a reader picks any listed directory, **Then** the directory exists at the repository root and the accompanying description accurately reflects what lives there.
3. **Given** the README before this change, **When** the section is added, **Then** all pre-existing README content is preserved unchanged (the change is purely additive).

### Edge Cases

- **Directory set drift**: If top-level directories are later added or removed, the section can become stale. The section should list only directories that exist at authoring time and be easy to update; it is not expected to auto-update.
- **Depth creep**: The section is a one-level map only. It must not attempt to document nested subdirectories, which would duplicate existing reference docs and rot quickly.
- **Duplication with existing docs**: A fuller source tree already exists at `docs/reference/project-structure.md`. The README section must stay short and, where helpful, point readers to that fuller reference rather than reproduce it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The README MUST contain a new section titled "Project layout".
- **FR-002**: The section MUST list the top-level directories of the repository that exist at authoring time (`bench`, `blueprints`, `demos`, `docker`, `docs`, `infra`, `mobile`, `ops`, `samples`, `scripts`, `specs`, `src`, `tests`, `walkthroughs`).
- **FR-003**: Each listed directory MUST have a brief (single-line) description of its purpose.
- **FR-004**: The change MUST be additive only — no existing README content is modified or removed, and no code, configuration, or behaviour changes are introduced.
- **FR-005**: The section SHOULD reference the fuller source-tree document (`docs/reference/project-structure.md`) so readers who need more detail know where to go.
- **FR-006**: Descriptions MUST be accurate against the actual contents of each directory at authoring time.
- **FR-007**: The section MUST be placed at a sensible location within the README's existing heading flow without disrupting the document's structure.

### Key Entities

- **Project layout section**: A README subsection consisting of a heading and a list of top-level directory entries, each pairing a directory name with a one-line purpose description.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reader can identify the purpose of any top-level directory from the README in under 30 seconds without opening any other file.
- **SC-002**: 100% of directories listed in the section exist at the repository root, and 100% of top-level directories present at authoring time are represented (no omissions, no phantom entries).
- **SC-003**: The change touches documentation only — zero source, test, or configuration files are modified.
- **SC-004**: All README content present before the change remains present and unchanged after it.

## Assumptions

- "Top-level directories" means the immediate child directories of the repository root, not nested paths.
- The audience is developers and contributors orienting to the codebase, not end users.
- A single-line purpose per directory is sufficient; multi-paragraph descriptions are out of scope and belong in `docs/reference/project-structure.md`.
- The directory list reflects the repository at authoring time and is maintained manually thereafter; automated synchronisation is out of scope.
- The README is the canonical repository `README.md` at the repository root.
