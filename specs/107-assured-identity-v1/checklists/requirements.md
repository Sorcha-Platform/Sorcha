# Specification Quality Checklist: Assured Identity v1

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-04-20
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

## Validation Notes

### Iteration 1 — passed with two minor fixes applied inline

**Initial issues caught and fixed:**

1. **Protocol-name leak in FR-016** — original wording referenced "OpenID4VCI pre-authorised code flow" in a parenthetical after "external HAIP wallet". The protocol name is implementation detail that adds nothing to the user-facing requirement (the outcome — holder chooses at claim time — is what the FR asserts). Rephrased to just "external HAIP wallet" with the parenthetical removed. The protocol specifics remain in Assumptions, which is the appropriate place.

2. **Framework-name leak in SC-012** — original wording was "zero new Blazor components". "Blazor" is a specific UI framework and leaks implementation detail into a success criterion. Rephrased to "zero new bespoke UI components". The metric stays measurable (count of new components added for this screen) without naming the framework.

**Domain language retained intentionally:**

- **"Wallet"**, **"Verifiable Credential"**, **"external HAIP wallet"**, **"persona profile"** — all inherited as user-facing Sorcha terminology from the Feature 103 spec's resolution of the same question. Stakeholders reading this spec will have encountered them in Feature 103 already; replacing them would fragment the platform's vocabulary.
- **"SD-JWT VC"**, **"OpenID4VCI"**, **"OpenID4VP `direct_post`"**, **"KB-JWT"** — appear only in Assumptions (where 103's spec also admits them) to document the prior-feature platform substrate this spec depends on. They do not appear in FRs or SCs.
- **"sorcha-agent"** — the autonomous actor framework name, user-visible in walkthrough authoring documentation. Retained.
- **"register-native delivery"** — user-facing Sorcha capability name from Feature 106. Retained.

**Coverage check:**

| Workstream | User Story | Functional Requirements | Success Criteria |
|---|---|---|---|
| 1 — Renderer polish | US-1 (enables) | FR-001 to FR-012 | SC-003, SC-004, SC-005, SC-007, SC-012 |
| 2 — Assured Identity credential + blueprint | US-1 | FR-013 to FR-021 | SC-001, SC-003 |
| 3 — Driving Licence | US-2 | FR-022 to FR-026 | SC-002, SC-005, SC-006 |
| 4 — Unattended assessor | US-3 | FR-027 to FR-030 | SC-008 |
| 5 — Consolidated walkthrough | US-1, US-2 | FR-031 to FR-035 | SC-001, SC-002, SC-011 |
| 6 — Cross-peer smoke | US-4 | FR-036 to FR-040 | SC-009, SC-013 |
| 7 — Consolidation & cleanup | US-5 | FR-041 to FR-044 | SC-010, SC-011, SC-014 |

Every functional requirement maps to at least one user story's acceptance scenarios; every success criterion maps to at least one workstream. No orphans.

**Assumption traceability:**

Every assumption in the Assumptions section either (a) names the prior feature it depends on (Features 098, 101, 102, 103, 104, 106, 092) or (b) cites an external industry standard (ISO/IEC 19794-5, ISO 18013-5, ICAO Doc 9303). No "we decided" assumptions without a cite.

## Notes

- All checklist items pass after one validation iteration. Spec is ready for `/speckit.clarify` (likely no-op given prior brainstorming produced the design doc this spec was derived from) or directly for `/speckit.plan`.
- The feature is intentionally split into seven phases that map user stories to sequenced platform deliverables. This phasing is *informative* in the spec and will be made *operative* by the planning phase.
- The design doc at `docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md` is the authoritative rationale source; the spec is the implementation contract derived from it. They should stay in sync — any change to the spec's scope that is not already represented in the design doc should be back-propagated to the design doc in the same commit.
