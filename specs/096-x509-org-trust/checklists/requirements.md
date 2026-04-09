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

- [ ] No [NEEDS CLARIFICATION] markers remain *(intentional — see Notes below)*
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

- Two architectural clarifications are intentionally left in the spec's Clarifications section rather than collapsed to defaults:
  - **Q4.1**: CA signing key in the wallet domain vs a new first-class entity. Spec draft defaults to Option B (new first-class entity) but the Sorcha tradition of BIP32 purpose consolidation pulls toward Option A. User ruling required.
  - **Q4.2**: EKU OID set for HAIP credential issuer Org Certs. Spec draft defaults to Option D (no EKU, defer to HAIP 1.1). User ruling required — specifically influenced by whether a named partner (GOV.UK Wallet, EUDI Wallet) has a concrete EKU requirement.
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
- The "No [NEEDS CLARIFICATION] markers" checklist item is intentionally unchecked. Q4.1 and Q4.2 are genuine architectural questions that affect planning shape. Closing them requires a user ruling, not a planner's best guess.
- Items requiring updates before `/speckit.plan`: Q4.1 and Q4.2 rulings must be captured.
