# Phase 0 Research: Add Missing XML Doc Summaries to One Public C# Class

All Technical Context unknowns are resolved below. There were no `NEEDS CLARIFICATION`
markers; the open questions were how to find a candidate, how to verify completeness given
the repo's warning configuration, and what form summaries should take.

## Decision 1 — How missing-doc warnings behave in this repo

**Decision**: Treat the `/// <summary>` convention as a *convention*, verified by a
doc-coverage scan, not by the compiler.

**Rationale**: `CS1591` ("Missing XML comment for publicly visible type or member") is
explicitly listed in `NoWarn` in every relevant area's `Directory.Build.props`:

- `src/Core/Directory.Build.props` → `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>`
- `src/Common/Directory.Build.props` → `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>`
- `src/Services/Directory.Build.props` → `<NoWarn>$(NoWarn);CS1591;CS1573;CS1572;CS1574;CS1734;CS0419;CS1587</NoWarn>`
- `tests/` and `bench/` similarly enable `GenerateDocumentationFile` with CS1591 suppressed.

So the build does **not** emit a warning for a public member that lacks a summary.
Consequently:
- **SC-002** ("zero missing-XML-documentation build warnings after the change") is already
  true for any file because CS1591 is suppressed — the change cannot regress it, and the
  build must simply stay clean.
- Verifying **SC-001** (100% of public members documented) requires an *inspection / scan*,
  not the compiler.

**Alternatives considered**:
- *Temporarily removing CS1591 from `NoWarn` to let the build flag gaps* — rejected: it
  would surface warnings across the whole solution, not just the target file, and would
  itself be an out-of-scope change to a shared props file (violates FR-004).
- *Relying on the build alone* — rejected: it cannot detect missing summaries while CS1591
  is suppressed.

## Decision 2 — How to find the single candidate file

**Decision**: Use a doc-coverage scan over `src/**/*.cs` that flags a public type or public
member whose nearest preceding non-blank, non-attribute line is not a `///` comment. Choose
one file that declares a **single** public type with multiple undocumented public members.

**Rationale**: The spec requires "one public C# class" (FR-001) and meaningful value with a
minimal change. A single-type file keeps the diff cohesive and unambiguous (a file with many
types invites scope creep). The scan run during planning produced 328 candidate files;
single-type, high-gap files are the best fit.

**Recommended candidate**:
`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs`
— one `public class CredentialApiService : ICredentialApiService`, ~60 of 62 public members
undocumented. Because the class implements `ICredentialApiService`, members that directly
implement a documented interface member are natural `/// <inheritdoc/>` candidates.

**Alternatives considered**:
- Multi-type files (e.g. `IRegisterServiceClient.cs`, which bundles 14 types) — rejected:
  documenting all of them blurs the "one class" boundary; documenting only one type within a
  multi-type file leaves the file partially undocumented and the selection arbitrary.
- DTO/model files under `src/Apps/Sorcha.Cli/Models/` — acceptable fallbacks; simpler
  summaries (plain data holders) but lower IntelliSense value than a service class.

## Decision 3 — Form of the added summaries

**Decision**: Add well-formed `/// <summary> ... </summary>` blocks describing each member's
purpose at the API level. Use `/// <inheritdoc/>` where the member directly implements a
documented interface/base member and inheriting that doc is accurate. Preserve any existing
doc comments untouched; add only missing summaries; do not add `<param>`/`<returns>` unless
trivially accurate (they are not required and CS1573 is suppressed).

**Rationale**: Matches the spec edge cases (FR-003, FR-006, FR-007) and the project
documentation policy. `<inheritdoc/>` avoids duplicating interface prose and stays accurate
when the interface is already documented. Summaries describe observable intent derivable from
the signature and surrounding code — no unverifiable implementation claims (SC-005).

**Alternatives considered**:
- Mechanically restating the member name as the summary — rejected by FR-003 ("without
  restating its name verbatim").
- Adding full `<param>`/`<returns>`/`<exception>` tags everywhere — rejected as scope creep;
  not required by the convention and risks inaccuracy.

## Decision 4 — Verification strategy

**Decision**: After editing, verify with (a) `git diff --stat` showing exactly one file
changed; (b) `git diff` showing only added `///` lines (no executable-line changes); (c) a
re-run of the doc-coverage scan against the chosen file showing zero remaining undocumented
public members; (d) `dotnet build` of the owning project remaining warning-free.

**Rationale**: These map one-to-one onto SC-001..SC-005 and are the only reliable checks
given CS1591 suppression (Decision 1).

**Alternatives considered**: Build-only verification — rejected per Decision 1.
