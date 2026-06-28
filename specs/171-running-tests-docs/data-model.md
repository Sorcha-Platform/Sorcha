# Phase 1 Data Model: Running the Tests README Section

This feature introduces no runtime data, persistence, or code entities. The only "entity" is a documentation block, modeled here for completeness per the spec's Key Entities.

## Entity: README "Running the tests" section

A new, short Markdown block in the root `README.md`.

| Attribute | Type | Description | Validation rule |
|-----------|------|-------------|-----------------|
| Heading/title | Markdown `##` heading | The section title | MUST read exactly "Running the tests" (FR-001) |
| Description | One-line prose | Brief framing of what the command does | SHOULD be a single short sentence (FR-006) |
| Command | Fenced code block | The suite-runner command | MUST be `dotnet test` in a copyable fenced block; MUST be accurate (FR-002, FR-003) |
| Deep-guide pointer | Markdown link | Reference to fuller guidance | SHOULD link to `DEVELOPMENT.md` for detail (FR-006); MUST NOT duplicate it |

### Relationships

- **Complements** the existing `## Development` section (which links to `DEVELOPMENT.md`). The two coexist; the new section is the quick path, `## Development` / `DEVELOPMENT.md` is the deep path.

### State transitions

N/A — static documentation. The section is either absent (before) or present (after). No lifecycle.

### Invariants (from requirements)

- Additive only: no pre-existing README section is removed or semantically changed (FR-004, SC-004).
- Documentation-only: zero non-documentation files change (FR-005, SC-003).
- Short: heading + one-line description + command (+ optional pointer) (FR-006).
