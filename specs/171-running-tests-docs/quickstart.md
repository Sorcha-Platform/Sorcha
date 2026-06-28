# Quickstart / Validation Guide: Running the Tests README Section

This guide validates the feature end-to-end: that the new README section exists, is correctly titled, contains the accurate test command, and that the change is additive and documentation-only.

## Prerequisites

- Repository checked out on branch `171-running-tests-docs`.
- .NET 10 SDK installed (only needed for Scenario C, which actually runs the documented command).
- A Markdown viewer or GitHub rendering for visual review.

## Validation scenarios

### Scenario A — Section is present and correctly titled (FR-001, AC-1)

1. Open the root `README.md`.
2. Scan the headings (or search for "Running the tests").
3. **Expected**: A `## Running the tests` section exists, near the `## Development` section.

```bash
grep -n "^## Running the tests" README.md
```

**Pass when**: the grep returns exactly one match.

### Scenario B — Documented command is present and copyable (FR-002, AC-2)

1. Read the new section.
2. **Expected**: a fenced code block containing `dotnet test`.

```bash
# The section contains a fenced 'dotnet test' command
grep -n "dotnet test" README.md
```

**Pass when**: `dotnet test` appears within the new section's fenced block.

### Scenario C — Command is accurate and actually runs the suite (FR-003, SC-002, AC-3)

1. From the repository root, copy the command from the section verbatim and run it (prerequisites met).

```bash
dotnet test
```

**Pass when**: the test suite executes (tests start running). A passing/failing test outcome is out of scope — the point is the command is not stale/typo'd and the suite runs.

### Scenario D — Change is additive and documentation-only (FR-004, FR-005, SC-003, SC-004)

1. Inspect the diff for this feature.

```bash
git diff --stat master -- README.md          # only README.md should appear
git diff --name-only master                  # confirm no non-doc files changed
```

**Pass when**:
- Only `README.md` (and the `specs/171-running-tests-docs/` planning docs) are modified.
- The `README.md` diff is purely an insertion — no existing section is removed or reworded (review the `git diff README.md` hunks to confirm only added lines around the new section, plus at most minimal surrounding navigation).

### Scenario E — No duplication/contradiction with existing Development guidance (edge case, FR-006)

1. Confirm the existing `## Development` section and its `DEVELOPMENT.md` pointer remain intact.
2. **Expected**: the new section references `DEVELOPMENT.md` for deeper detail rather than reproducing it.

**Pass when**: `## Development` is unchanged and the new section defers depth to `DEVELOPMENT.md`.

## Success-criteria mapping

| Criterion | Validated by |
|-----------|--------------|
| SC-001 (find command in <30s) | Scenario A + B (single scan locates titled section + command) |
| SC-002 (command runs) | Scenario C |
| SC-003 (docs-only) | Scenario D |
| SC-004 (existing content preserved) | Scenario D + E |

See [data-model.md](./data-model.md) for the section's attribute rules and [research.md](./research.md) for the command/placement decisions.
