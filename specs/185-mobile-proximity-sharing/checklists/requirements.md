# Specification Quality Checklist: Mobile proximity credential sharing

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-07-13
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

**Validation pass 1 findings (fixed inline):**

- *Implementation leakage*: the first draft named BLE, mdoc, COSE_Mac0, SD-JWT and the loopback transport
  throughout the requirements. Rewritten to state the observable behaviour ("no network on either device",
  "a third party observing the radio traffic learns no credential content", "without physical devices",
  "the international standard format and Sorcha's native format"). The technology choices remain settled —
  they live in the **design of record**, which the spec links rather than restates.
- *SC-003 phrasing*: "matches ISO test vectors" was replaced with "published reference data for the
  standard", keeping the criterion verifiable without naming the artefact.

**Deliberate retentions:**

- The spec names ISO 18013-5 in its **title** and links the design of record. This is intentional: the
  feature's whole purpose is conformance to a named standard, and hiding that name would obscure the point
  rather than clarify it. No requirement or success criterion depends on knowing the standard's internals.
- **No [NEEDS CLARIFICATION] markers were raised.** Every decision that would have warranted one — protocol,
  roles, formats, device-auth mode, BLE role, evidence bar — was settled with the user before the design was
  written, and is recorded in the design of record.

**Open risk carried forward (not a spec defect):** the evidence bar (SC-008) is self-consistency between our
own two devices. This is called out explicitly in Assumptions so that no reader mistakes it for interop
evidence. SC-003 is the compensating control.
