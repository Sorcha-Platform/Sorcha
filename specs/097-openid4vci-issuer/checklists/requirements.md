# Specification Quality Checklist: OpenID4VCI Issuer Endpoint (HAIP)

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-04-09
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

- Six user stories cover: end-to-end citizen issuance via QR (US1), issuer metadata publication (US2), Blueprint author ergonomics (US3), token endpoint (US4), credential endpoint with JWT proof of possession (US5), and the new Sorcha.Haip.Service as an eighth first-class service (US6). Priorities P1/P1/P1/P1/P1/P2.
- Per Phase 2 D1 Option A: new eighth service `Sorcha.Haip.Service`, thin orchestrator, holds no keys, calls Wallet Service for signing and Blueprint Service for offer lifecycle.
- Consumes spec 094 (cnf, co-key), spec 095 (IETF status claim), spec 096 (x5c chain). Every HAIP-path credential carries all three.
- Inline decisions (non-architectural):
  - HAIP 1.0 MTI pre-authorized code flow only. Authorization code flow with browser redirect deferred.
  - Access token and pre-authorized code TTLs default 5 minutes each. Configurable.
  - `c_nonce` is single-use from the holder's perspective; the nonce endpoint issues a fresh one on demand.
  - Rate limit policies `HaipToken` and `HaipCredential` added to the existing `RateLimitPolicies` pattern.
  - DPoP deferred. Bearer tokens only.
  - One credential per credential endpoint call. Batch issuance deferred.
  - `tx_code` on pre-authorized code flow deferred.
  - Deferred credential endpoint deferred. Synchronous issuance only in this spec.
  - `TargetAudience` on `CredentialIssuanceConfig` is the single Blueprint-level config knob for HAIP-path routing. Default `SorchaInternal` preserves existing behaviour.
  - `RecipientParticipantId` is advisory for HAIP-path credentials; the actual recipient is whoever scans the QR.
- Edge cases cover pre-authorized code races, expired codes, cancelled Blueprint actions, rotated issuer keys mid-flow, and URL rewriting behind the API Gateway.
- All checklist items pass on this iteration.
