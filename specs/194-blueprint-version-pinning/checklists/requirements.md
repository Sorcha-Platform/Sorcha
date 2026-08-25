# Specification Quality Checklist: Blueprint Version Pinning

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-23
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Validation notes

**Iteration 1 findings, all corrected before this checklist was marked:**

- *Implementation leakage.* The first draft named the carrier (the routing-decision record), the
  hashing algorithm and the specific error code. All were replaced with the capability being
  required — FR-009 now says the assertion must be authenticated inside the signed material, without
  naming which record carries it. The design document remains the place those decisions live.
- *An untestable success criterion.* "The pin is correct" was replaced by SC-001/SC-003, which are
  each verified by an observable outcome (an application completes; a new requirement is actually
  enforced) rather than by inspecting stored state.
- *A guard requirement that could be satisfied vacuously.* FR-010 originally said the signing
  coverage "must be tested". A test written after the fix has never been observed to fail, so it
  proves nothing. It now requires the guard be derived from the shape of the signed material rather
  than a hand-maintained list, and SC-008 requires every guard to have been watched failing.
- *An edge case stated as prose, not as behaviour.* "Instances predating this feature" now carries
  the three properties that make it checkable: one fallback, applied identically on every derivation
  path, and countable.

## Deliberate deviations from the template

- A short **"The problem, in one paragraph"** section precedes the user stories. This feature's
  entire justification is a defect that is invisible from the outside; a reader who does not
  understand the defect cannot judge whether the requirements are sufficient.
- A **"Findings from verification of the design"** section closes the spec. These are not
  requirements and are marked as such, but three of the four change the size of the work and one
  identifies dead code that must be resolved rather than left beside the new mechanism. Recording
  them in the spec is what stops them being rediscovered during planning.
- **"Explicitly out of scope"** carries reasons, not just exclusions — specifically for the
  no-bespoke-upgrade-gate decision, which a later reader would otherwise be likely to add in good
  faith believing it an oversight.

## Notes

- Zero clarification markers. The three questions the design left open were resolved as follows:
  - *Retain or drop the human-facing version label?* Retained, and required by FR-019 to be derived
    from the pin so the two cannot disagree — the design's own recommendation.
  - *Does anything else resolve a definition by id assuming latest?* Swept before the spec was
    written. Execution-path sites are in scope; authoring, catalogue and administrative surfaces are
    correctly latest and are excluded explicitly.
  - *Should the pre-feature fallback survive?* Resolved by establishing that the rollout recreates
    the workflow database but **not** the register, so un-pinned sealed submissions do survive and
    the fallback is required rather than optional. Its removal trigger is stated as a condition, not
    a date.
- Ready for `/speckit.plan`.
