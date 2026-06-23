# Phase 1 Data Model: Add Missing XML Doc Summaries to One Public C# Class

This feature introduces **no runtime data model** — it adds documentation comments to one
existing type. The "entities" below are conceptual artifacts used by the planning and
verification process (mirroring the spec's Key Entities), not persisted types.

## Entity: Target Class File

The single `.cs` file selected for documentation.

| Attribute | Description | Validation Rule |
|-----------|-------------|-----------------|
| `filePath` | Repository-relative path to the chosen file | Exactly one path; under `src/`; declares a public type (FR-001, FR-004) |
| `declaredType` | The public type the file declares | Single public type recommended (cohesive diff); the type itself must end documented (FR-002) |
| `undocumentedMembers` | Set of public members lacking `/// <summary>` before the change | Must be non-empty (the file is a candidate only if it has gaps) |

**State transition**: `Candidate (has undocumented public members)` → `Documented (every
public member, including the type, has a /// <summary> or accurate /// <inheritdoc/>)`.
No intermediate persisted state.

## Entity: Public Member

A type, method, or property with `public` accessibility forming the documented API surface.

| Attribute | Description | Validation Rule |
|-----------|-------------|-----------------|
| `kind` | `type` \| `method` \| `property` | Only public members are in scope (Assumptions); protected/internal/private excluded |
| `hasSummaryBefore` | Whether a `/// <summary>` existed prior to the change | Members where `true` are left untouched (FR-006) |
| `summaryAfter` | The added or pre-existing summary text | Concise, accurate, not a verbatim restatement of the name; well-formed XML (FR-003, FR-007). `/// <inheritdoc/>` permitted when the member directly implements a documented member and that is accurate (FR-002 note, Assumptions) |

**Invariant (post-change)**: For every public member of the target type,
`hasSummaryBefore == true` OR a new accurate summary/`<inheritdoc/>` has been added — i.e.
100% coverage (SC-001), with previously-documented members unchanged (SC-004, FR-006).

## Non-goals (explicitly out of the model)

- No new persisted entities, database schema, DTOs, or migrations.
- No changes to member signatures, accessibility, or behavior (FR-005).
- No second file or shared props/config changes (FR-004).
