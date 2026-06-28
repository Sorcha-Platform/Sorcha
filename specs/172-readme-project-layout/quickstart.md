# Quickstart / Validation: README Project Layout Section

A run guide to prove the feature works end-to-end. See [data-model.md](./data-model.md) for the content schema and invariants, and [spec.md](./spec.md) for requirements.

## Prerequisites

- A checkout of the repository on branch `172-readme-project-layout`.
- A shell with `git`, `ls`, `grep`, `sort`, `comm` (standard on Linux/macOS).

## What "done" looks like

`README.md` contains a `## Project layout` section listing all 14 root directories with one-line purposes and a pointer to `docs/reference/project-structure.md`; nothing else in the README changed.

## Validation scenarios

### Scenario A — Section exists and is correctly headed (FR-001, Acceptance 1)

```bash
grep -n '^## Project layout' README.md
```

**Expected**: exactly one match.

### Scenario B — Completeness & existence (FR-002, FR-006, SC-002, INV-1/INV-2)

Compare the directories named inside the section against the live repository root. The section names directories as `` `name/` ``.

```bash
# Directories actually present at the root
ls -d */ | sed 's#/##' | sort > /tmp/actual_dirs.txt

# Directory names mentioned inside the Project layout section
awk '/^## Project layout/{f=1;next} /^## /{f=0} f' README.md \
  | grep -oE '`[a-z][a-z0-9-]*/`' | tr -d '`/' | sort -u > /tmp/listed_dirs.txt

# Any difference is a failure (left-only = missing from README, right-only = phantom)
comm -3 /tmp/actual_dirs.txt /tmp/listed_dirs.txt
```

**Expected**: `comm -3` prints **nothing** — every root directory is listed and no listed name is a phantom.

### Scenario C — Additive only (FR-004, SC-003, SC-004, INV-3)

```bash
# Only README.md is modified
git diff --name-only master...HEAD

# The diff adds lines only — no pre-existing README lines removed/changed
git diff master...HEAD -- README.md | grep '^-' | grep -v '^---'
```

**Expected**: the first command lists `README.md` (and the `specs/172-...` planning docs) and **no** source/test/config files; the second command prints **nothing** (no removed lines — purely additive).

### Scenario D — Fuller-reference pointer (FR-005)

```bash
awk '/^## Project layout/{f=1;next} /^## /{f=0} f' README.md \
  | grep -c 'docs/reference/project-structure.md'
```

**Expected**: `1` or more — the section links to the fuller source tree.

### Scenario E — Single-level map (INV-4, Edge: depth creep)

Manual check: confirm no entry references a nested path (e.g. `src/Services/...`). Each entry names one top-level directory only.

## Success mapping

| Scenario | Requirements covered |
|----------|----------------------|
| A | FR-001, Acceptance 1 |
| B | FR-002, FR-003, FR-006, SC-001, SC-002, Acceptance 2 |
| C | FR-004, SC-003, SC-004, Acceptance 3 |
| D | FR-005 |
| E | FR-007 (placement), Edge: depth creep |
