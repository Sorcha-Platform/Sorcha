# Quickstart / Validation Guide: Add Missing XML Doc Summaries to One Public C# Class

This guide proves the feature end-to-end: one public class fully documented, exactly one
file changed, no executable code altered. See [plan.md](./plan.md), [research.md](./research.md),
and [data-model.md](./data-model.md) for rationale. Run from the repository root.

## Prerequisites

- .NET 10 SDK (`dotnet --version` → 10.x)
- Clean working tree on branch `156-add-xml-doc-summaries`

## Step 1 — Select the target file (FR-001)

Either use the recommended candidate or re-run the doc-coverage scan to pick another
single-type file with undocumented public members.

Recommended candidate:

```text
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs
```

To regenerate the candidate shortlist (single public type, ≥3 undocumented public members),
run the doc-coverage scan over `src/**/*.cs` (a member is "undocumented" when its nearest
preceding non-blank, non-attribute line is not a `///` line). The scan used during planning
surfaced 328 candidates; prefer a file declaring exactly one public type.

**Expected**: a single `.cs` path whose public type and several public members lack
`/// <summary>`.

## Step 2 — Confirm the gap before editing

Record which public members lack a summary (the "before" set). For the recommended file,
~60 of 62 public members are undocumented and the class implements `ICredentialApiService`.

**Expected**: a non-empty set of undocumented public members.

## Step 3 — Add the summaries (FR-002, FR-003, FR-006, FR-007)

Add a `/// <summary> ... </summary>` to every undocumented public member and to the type
declaration if it is undocumented. Where a member directly implements a documented
interface member and inheriting that doc is accurate, use `/// <inheritdoc/>`. Leave any
already-documented member untouched. Do not change signatures, accessibility, or logic.

**Expected**: only `///` comment lines are added; no executable lines change.

## Step 4 — Verify (maps to Success Criteria)

| Check | Command | Expected outcome | Criterion |
|-------|---------|------------------|-----------|
| Exactly one file changed | `git diff --stat` | One `.cs` file listed | SC-003 |
| Only comment lines added | `git diff` | Added lines are all `///`; no removed/changed executable lines | SC-004 |
| 100% public members documented | Re-run the doc-coverage scan against the chosen file | Zero remaining undocumented public members | SC-001 |
| Build stays clean | `dotnet build` on the owning project | Build succeeds, no new warnings | SC-002 / Constitution V |
| Summaries are accurate | Manual review of the diff against each member signature | Each summary correctly describes its member; no verbatim-name restatement; no unverifiable claims | SC-003, SC-005 |

> Note (from [research.md](./research.md)): `CS1591` is suppressed via `NoWarn`, so the
> build does **not** flag missing summaries. SC-001 is verified by the scan, not the build;
> SC-002 is satisfied because the suppressed warning cannot appear.

## Done when

- `git diff --stat` shows exactly one changed `.cs` file.
- The doc-coverage scan reports zero undocumented public members for that file.
- `dotnet build` succeeds with no new warnings.
- Diff review confirms documentation-only additions with accurate summaries.
