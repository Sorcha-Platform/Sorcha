# Specification Quality Checklist: OpenID4VP Verifier Endpoint (HAIP)

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-04-09
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain *(Q6.1 resolved by user ruling — see Notes)*
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

- This spec supersedes the OID4VP story in spec 039 (US3, US4, FR-013 through FR-018) while carrying forward every other 039 requirement or pointing at earlier specs in the 093–098 series that already addressed them. The amendment note at the tail of the spec lists each 039 requirement and its status.
- Six user stories cover: end-to-end HAIP presentation (US1), Blueprint author ergonomics (US2), Authorization Request generation (US3), direct_post verification pipeline (US4), Blueprint action resume on verification outcome (US5), cross-device QR and same-device deep link (US6). Priorities P1/P1/P1/P1/P2/P2.
- Per Phase 2 D1 Option A, the verifier lives in the same `Sorcha.Haip.Service` as the issuer from spec 097. This spec reuses spec 097's deployment topology, rate limits, and API Gateway routing — no new service is created.
- One architectural clarification was raised and **has been resolved by user ruling**: **Q6.1 → Option C** (SignalR signal on ActionsHub per spec 089 minimal-disclosure policy, with polling as fallback for when SignalR delivery is unavailable). Rationale: loose coupling between Sorcha.Haip.Service and the Blueprint Service, reuse of existing SignalR infrastructure, matches the established Sorcha pattern for real-time coordination. Reflected in FR-030 and in the spec's Clarifications section.
- Inline decisions (non-architectural):
  - `direct_post` only. Other response modes deferred.
  - Both cross-device QR and same-device deep link supported (HAIP 1.0 requires both).
  - `client_id_scheme: x509_san_uri` (the HAIP-profile scheme when X.509 is in use).
  - Verifier signs its Request Object with the same classical HAIP signing key the issuer uses — one org identity, two roles on the same wallet and `x5c` chain.
  - DIF Presentation Exchange 2.0 for `presentation_definition`.
  - Both X.509 trust path (new, spec 096) and DID trust path (existing, spec 039) supported; credential supplies whichever it has.
  - Presentation Request TTL default 5 minutes, configurable.
  - Clock skew window ±60 seconds, matching spec 094.
  - `Denied` results do not leak claim values to preserve holder privacy against a hostile caller probing the verifier.
  - Blueprint execution `AwaitingExternalPresentation` state added as a new suspended state, reusing existing Blueprint action suspend/resume infrastructure.
  - `PresentationSource: HaipExternalWallet` on the Blueprint credential requirement model is the single Blueprint-level knob for HAIP routing, mirroring spec 097's `TargetAudience: HaipExternalWallet`.
  - Parity regression tests ensure the internal and HAIP verification paths produce identical outcomes for the same credential.
- All checklist items pass as of this iteration. Q6.1 ruling is captured in the spec's Clarifications section and in FR-030.
