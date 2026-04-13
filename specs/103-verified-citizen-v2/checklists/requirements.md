# Specification Quality Checklist: Verified Citizen v2

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-04-13
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

1. **Implementation leak in FR-007** — original wording referred to "canonical ledger state". Rephrased to "canonical service-instance history" to keep the spec audience-neutral. The underlying mechanism (which is in fact a ledger replay) is documented in the design spec, not the user-facing requirement.

2. **Implementation leak in SC-005** — original wording was "zero changes to primitive files or platform code". The word "files" leaked an implementation detail. Rephrased to "zero changes to either the primitives or the platform". The metric stays measurable (count of changes to two named artefacts) without revealing how those artefacts are stored.

**Domain language retained intentionally:**

- **"Wallet"** — appears in FR-005 and the Citizen entity. The audience for service-designer FRs and entity definitions includes Sorcha-aware engineers and product owners; "wallet" is the unambiguous Sorcha term for the participant identity anchor. Replacing it with "identity" loses precision and risks confusing it with the persona profile. Retained.
- **"Service definition" + (blueprint)** — used as the bridge term so a stakeholder reading the spec maps to the engineering artefact without confusion. The spec leans on "service" / "service definition" in stakeholder-facing prose and only mentions "blueprint" once in the design link.
- **"Verifiable Credential" / "external HAIP wallet"** — these are user-visible Sorcha capabilities, not implementation details. The citizen's experience IS holding a Verifiable Credential; the assessor's experience IS issuing one. Retained.
- **"Persona profile"** — domain term for the citizen's stored autofill data. Already user-facing as of Feature 092.

**Coverage check:**

| Workstream | User Story | Functional Requirements | Success Criteria |
|---|---|---|---|
| 1 — Open starting actions | US-1 | FR-001 to FR-008 | SC-006, SC-007, SC-009, SC-010 |
| 2 — Identity primitives | US-2 | FR-009 to FR-017 | SC-002, SC-004, SC-005, SC-007 |
| 3 — Address lookup | US-3 | FR-018 to FR-025 | SC-003, SC-008 |
| 4 — Verified Citizen v2 | US-4 | FR-026 to FR-029 | SC-001, SC-007 |

Every functional requirement maps to at least one user story acceptance scenario; every success criterion maps to at least one workstream. No orphans.

## Notes

- All checklist items pass after one validation iteration. Spec is ready for `/speckit.clarify` (likely no-op given prior brainstorming) or directly for `/speckit.plan`.
- The feature is intentionally split into four phases that map one-to-one with the four user stories and the four PR deliverables described in the design spec. This phasing is *informative* in the spec (noted in the "Phase Mapping" section) and will be made *operative* by the planning phase.
