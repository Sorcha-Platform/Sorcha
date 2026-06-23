# Implementation Plan: Add Missing XML Doc Summaries to One Public C# Class

**Branch**: `156-add-xml-doc-summaries` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/156-add-xml-doc-summaries/spec.md`

## Summary

Select exactly one public C# class whose public surface (the type and/or its public
methods and properties) is missing `/// <summary>` XML documentation, and add concise,
accurate summaries to every undocumented public member of that file only. The change is
documentation-comment-only: no executable code, signatures, accessibility, or behavior
changes, and no file other than the chosen `.cs` is touched.

**Technical approach**: A doc-coverage scan over `src/**/*.cs` enumerates files containing
a public type with multiple public members whose immediately-preceding non-attribute line
is not a `///` comment. From the resulting shortlist, pick one cohesive single-type file,
add `/// <summary>` blocks (using `/// <inheritdoc/>` where a member directly implements a
documented interface member and that is the accurate intent), and verify by re-running the
scan against the chosen file plus a clean `dotnet build`.

**Recommended candidate** (final selection is the first implementation step — FR-001):
`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs`
— a single `public class CredentialApiService : ICredentialApiService` with ~60 of 62
public members undocumented. Most members implement `ICredentialApiService`, making it an
ideal fit for the spec's `<inheritdoc/>` path. Alternatives from the scan shortlist include
single-type DTO/model files under `src/Apps/Sorcha.Cli/Models/`.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: None new. This is an inline-documentation change only; no packages,
APIs, or runtime dependencies are added or modified.

**Storage**: N/A

**Testing**: Verification is via `dotnet build` (must remain warning-free) plus a repeatable
doc-coverage scan of the single chosen file. No new unit/integration tests are added —
there is no behavior to test (FR-005). Existing tests must continue to pass unchanged.

**Target Platform**: N/A (source-level change; affects generated XML doc / IntelliSense /
OpenAPI quality only)

**Project Type**: Single-file source edit within the existing .NET solution.

**Performance Goals**: N/A — no runtime impact.

**Constraints**:
- Exactly one `.cs` file may change (FR-004 / SC-003).
- Zero executable-code lines change — additions are `///` comment lines only (FR-005 / SC-004).
- **CS1591 (missing XML doc on public member) is suppressed via `NoWarn` across `src/Core`,
  `src/Common`, and `src/Services` Directory.Build.props** (and tests/bench). Missing
  summaries therefore do **not** currently surface as build warnings. SC-002 ("zero
  missing-XML-documentation warnings") is satisfied by convention, and verification of doc
  completeness must use the doc-coverage scan rather than relying on the build to flag gaps.
  See [research.md](./research.md).

**Scale/Scope**: One file; the type itself plus its public methods/properties. The
recommended candidate has ~60 undocumented public members.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| III. API Documentation ("All public APIs MUST have XML documentation") | **Directly advances** — this feature exists to close a documentation gap. ✅ |
| V. Code Quality ("No compiler warnings in Release builds") | Preserved — change adds only `///` lines; build stays warning-free. ✅ |
| IV. Testing Requirements | N/A — no behavior change, so no new tests required; existing suite must still pass. ✅ |
| I / II / VI / VII / VIII | Not engaged — no architecture, security, blueprint, domain-model, or telemetry surface is touched. ✅ |

**Result**: PASS. No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/156-add-xml-doc-summaries/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── checklists/          # Pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

No `contracts/` directory is generated: this change exposes no new external interface,
endpoint, command, or schema. It adds inline documentation to one existing type only.

### Source Code (repository root)

Exactly one existing file is modified — the chosen target class. No new directories or files
are created. The recommended candidate sits at:

```text
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/
└── CredentialApiService.cs   # single public class; ~60 undocumented public members
```

Alternative single-type candidates from the doc-coverage scan (any one would satisfy the
spec) include files under `src/Apps/Sorcha.Cli/Models/` (e.g. `Wallet.cs`, `Credential.cs`).

**Structure Decision**: Single-file edit inside the existing solution layout. No structural
change to the repository. The target lives under `src/Apps/Sorcha.UI/Sorcha.UI.Components.User`
(the shared user-facing component library, Feature 122) per the recommended candidate.

## Complexity Tracking

> Not applicable — Constitution Check passed with no violations.
