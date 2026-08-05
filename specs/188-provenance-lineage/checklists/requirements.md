# Specification Quality Checklist: Provenance — trust-anchor and proof lineage

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-05
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

## Notes

Validation run 2026-08-05. Three issues were found and fixed inline before this checklist was marked
complete:

1. **Implementation detail leaked into requirements.** The first draft named the engine assembly,
   the three endpoint routes, the authorization policies and the UI project. All were removed from
   the spec proper — they are design decisions and belong in `plan.md`. FR-019 now states the access
   rule in terms of *who* ("administrators of the owning organisation") rather than *which policy*.

2. **Two success criteria were untestable as written.** "Recomputation proves tamper-evidence, not
   correctness" was a caveat, not an outcome; it became FR-005 (a requirement about what a check may
   claim) plus SC-003 (an outcome that is demonstrated by altering data and observing Failed).
   Similarly the roster caveat became FR-010 and the bidirectional SC-004.

3. **The performance rule was phrased as an implementation instruction** ("verification is
   on-demand, not on-list"). Restated as the outcome it protects: FR-021/FR-022 and SC-007, which
   are checkable without knowing how paging or caching work.

Two deliberate retentions, flagged so a reviewer does not "fix" them:

- **SC-003, SC-004 and SC-005 read as adversarial tests rather than user outcomes.** That is
  intended. The failure mode this feature must avoid is reporting Verified for a check that did not
  run, which is invisible to any test that only exercises the happy path. An outcome that cannot
  distinguish a working check from a decorative one is not worth measuring.

- **"Not verifiable" appears throughout as a first-class result.** It reads like hedging; it is the
  opposite. On a single-validator deployment — which is what local development and the current
  demonstration node both are — several checks genuinely cannot run, and a green tick there would be
  the feature's most serious possible defect.
