# Specification Quality Checklist: Citizen "My Applications" View

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-02
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

- Items marked incomplete require spec updates before `/speckit.clarify` or `/speckit.plan`

### Validation

Validated once, on 2026-08-02, against the spec as written. All items pass; no revision iterations were needed.

The one item that warranted a close read was **"no implementation details"**, because the incoming brief was code-level throughout (concrete type names, endpoint paths, file paths). Those specifics were deliberately kept out of the spec and deferred to `plan.md`; the **Settled Design Constraints** section states the same decisions in domain language instead. That section is retained on purpose — these are settled inputs to planning rather than open questions, and dropping them would invite re-litigation of choices already made with the requester.

The closest thing to a borderline call is **FR-008**, which references sign-ins that carry no wallet claim. That is auth-model vocabulary, but it is load-bearing: it names the exact condition under which a citizen's own applications were previously invisible to them, so stating it plainly is worth more than keeping the sentence abstract.

### Deliberate calls worth noting for planning

- **Zero [NEEDS CLARIFICATION] markers.** All three questions that would have warranted one — which surface, where the reason comes from, and how the new list sits against the existing actions list — were settled with the requester before specification. They are recorded under Settled Design Constraints.
- **FR-013 and FR-014 are the load-bearing pair.** Together they say: show the service's own wording or nothing. A generic fallback would be worse than silence, because a citizen told a plausible-but-invented reason cannot tell it apart from a real one.
- **FR-024 and SC-006 exist to protect a non-citizen user** whose queue this feature renames but must not otherwise disturb. Treat any change to that list's route, scope, or behaviour as out of scope.
- **SC-004 is a regression criterion**, not a feature criterion — it asserts an existing defect is not reproducible against the new view.
