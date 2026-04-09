# Specification Quality Checklist: X.509 Organisation Trust Integration

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-04-09
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain *(Q4.1 and Q4.2 resolved by user ruling — see Notes)*
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

- Two architectural clarifications were raised during drafting and **have been resolved by user ruling**:
  - **Q4.1** → **Option B** (new first-class Tenant CA entity with its own key storage). Rationale: end goal is to support importing signing keys from external sources (HSMs, eIDAS QTSPs), which requires storage independent of wallet HD derivation. Internally generated CA keys can still be derived under `sorcha:tenant-ca-signing` from a recovery seed for deterministic recovery.
  - **Q4.2** → **Option D** (no EKU extension on Org Certs; defer to HAIP 1.1 or named partner requirement). A follow-up operational spec will add a configurable EKU per trust provider when needed.
- Five user stories cover: external HAIP wallet trusts a Sorcha credential via X.509 (US1), tenant PKI provisioning (US2), DID ↔ X.509 identity binding (US3), org cert revocation via CRL (US4), pluggable trust provider swap (US5). Priorities P1/P1/P1/P2/P2.
- Inline decisions made (not flagged as architectural clarifications):
  - Two-level chain (Tenant Root → Org Cert). Three-level is an out-of-scope extension.
  - CRL only, no OCSP. OCSP deferred.
  - SAN URI carries the `did:sorcha:org:{walletAddress}` identifier, linking the X.509 and DID identities.
  - Subject CN carries the org's human-readable display name.
  - `x5c` header in JWS is the wire carrier for the chain, per RFC 7515 §4.1.6 and HAIP 1.0.
  - Default CA validity 10 years; default Org Cert validity 2 years; default CRL refresh 24 hours. All configurable.
  - Internal-path credentials do NOT carry `x5c`. The two trust stacks run genuinely in parallel on different credentials.
- Inline decision on org cert issuance triggering: enrolment requires `HaipIssuer` capability from spec 094 to already be set. No auto-enrolment; operator action is required.
- All checklist items pass as of this iteration. Q4.1 and Q4.2 rulings are captured in the spec's Clarifications section, in FR-002, FR-012, and in the Assumptions section.
