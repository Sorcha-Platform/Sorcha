# Feature Specification: Sorcha Conformance Oracle (SCO)

**Feature Branch**: `149-conformance-oracle`

**Created**: 2026-06-06

**Status**: Draft

**Input**: User description: "Sorcha Conformance Oracle — the 'ultimate proof': a correctness oracle that exercises every Sorcha capability at least once and asserts the platform's guarantees hold after every operation."

> Full design rationale and the locked brainstorming decisions: `docs/superpowers/specs/2026-06-06-sorcha-conformance-oracle-design.md`.

## Overview

Sorcha has 143 spec'd features and 11,088 tests, yet its correctness *under realistic conditions* is unproven: CI validates only unit/in-memory paths, and a recurring family of distributed "seal-window" defects (#119, #787, #814, #917, #585) shows the seal/consensus/replication paths fail in ways unit mocks never expose. The Conformance Oracle is a single instrument that (a) demonstrates every capability is exercised at least once, and (b) asserts the platform's core guarantees still hold after every operation — serving as both a confidence instrument and a live v1-correctness dashboard.

The "users" of this feature are the **engineering team** and the **release decision-maker**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Complete, traceable map of capabilities and guarantees (Priority: P1)

An engineer needs a single authoritative catalogue of *every* distinct Sorcha capability (deduplicated from the 143 spec folders) and *every* guarantee the platform claims, with each capability traceable back to its originating specs and forward to the guarantees it can affect.

**Why this priority**: Without an agreed coverage axis and guarantee set, "exercises every feature" and "asserts correctness" are undefined. This artifact alone supersedes the stale May-2026 roadmap and enumerates the correctness debt. It requires no test infrastructure.

**Independent Test**: Produce the Capability Registry and Invariant Catalogue; verify every one of the 143 specs maps to ≥1 capability, every capability links to ≥1 guarantee (or is explicitly surface-smoke-only), and every guarantee links to ≥1 capability that can violate it.

**Acceptance Scenarios**:

1. **Given** the 143 spec folders, **When** the registry is built, **Then** each spec is traceable to one or more capabilities and no spec is unmapped.
2. **Given** the Invariant Catalogue, **When** it is reviewed, **Then** every guarantee is tagged either Gate (must hold today) or Aspirational (known gap) with a linked issue for each Aspirational entry.
3. **Given** the two pillars, **When** cross-checked, **Then** there are no dead guarantees (no exercising capability) and no orphan capabilities (no linked guarantee and not marked surface-smoke-only).

---

### User Story 2 - Design-correctness proof with full coverage (Priority: P1)

An engineer runs a single proof that drives a deterministic sequence of operations touching every implemented capability at least once and, after every operation, re-checks the entire guarantee set against an abstract reference of how Sorcha *should* behave.

**Why this priority**: This is the "ultimate proof" in its purest, fastest form — design-level correctness plus 100% capability coverage, runnable with no infrastructure on every change. It is the core deliverable.

**Independent Test**: Run the proof; confirm it reports 100% coverage of implemented capabilities and that every Gate guarantee holds across the run; introduce a deliberate defect into the reference behavior and confirm the proof fails loudly.

**Acceptance Scenarios**:

1. **Given** the operation sequence, **When** the proof runs to completion, **Then** every implemented capability is recorded as exercised ≥1 and the coverage result is 100%.
2. **Given** any operation in the sequence, **When** it completes, **Then** all Gate guarantees are re-asserted and hold.
3. **Given** a deliberately injected guarantee violation, **When** the proof runs, **Then** it fails and names the violated guarantee and the operation that triggered it.
4. **Given** an implemented capability with no operation that exercises it, **When** the proof runs, **Then** coverage is <100% and the proof fails.

---

### User Story 3 - Adversarial exploration beyond the scripted path (Priority: P2)

An engineer wants assurance that guarantees hold not just along one curated storyline but across many randomized operation-and-fault orderings, with any counterexample automatically minimized to the smallest reproducing sequence.

**Why this priority**: A single linear path proves little about ordering and concurrency. Randomized exploration finds violations the scripted path misses; minimized counterexamples become permanent regression seeds.

**Independent Test**: Run the explorer for a bounded budget; confirm it explores varied orderings including fault events, and that any discovered violation is reported as a minimized, replayable sequence pinned as a regression seed.

**Acceptance Scenarios**:

1. **Given** a randomized run budget, **When** the explorer runs, **Then** it generates varied operation/fault orderings and re-checks all guarantees after each step.
2. **Given** a discovered violation, **When** the explorer halts, **Then** it emits the minimized reproducing sequence and adds it to the persistent seed corpus.

---

### User Story 4 - Exhaustive proof of the consensus & sealing core (Priority: P2)

A release decision-maker needs exhaustive (not sampled) assurance that the chain-integrity, consensus-safety, and seal-ordering guarantees cannot be violated within bounded conditions — the exact area of the recurring seal-window defects.

**Why this priority**: Randomized testing samples; it cannot prove the absence of a race. The seal/consensus core is the highest-risk, most-defect-prone area and is small enough to verify exhaustively within bounds.

**Independent Test**: Run the exhaustive check over the bounded state space (small numbers of validators and dockets, with message reordering/loss and concurrent proposals); confirm it explores all reachable states and reports no safety violation, and that it is kept in lock-step with the reference behavior.

**Acceptance Scenarios**:

1. **Given** the bounded configuration, **When** the exhaustive check runs, **Then** it explores all reachable states and reports either a concrete violating trace or a clean pass.
2. **Given** the reference behavior and the exhaustive model, **When** compared, **Then** their operation alphabets and guarantee statements are verified to match (no divergence between the two representations).

---

### User Story 5 - Implementation conformance under adversarial conditions (Priority: P2)

An engineer replays the same operation sequences against the real running platform under induced faults — node crashes, slow or partitioned peers, concurrent seals, clock advancement — and confirms the platform's observable behavior conforms to the same guarantees.

**Why this priority**: Design correctness is necessary but not sufficient; the running implementation must conform. This is where the Aspirational guarantees gain teeth and where the long-standing "distributed paths are only tested by hand" gap is closed. It is also the cross-node test harness previously deferred (#585).

**Independent Test**: Run the harness against a real multi-node deployment with fault injection enabled; confirm guarantees are checked on observable platform state after each operation, and that a known gap (e.g. durability under crash) reproduces as a tracked-red result rather than a silent pass.

**Acceptance Scenarios**:

1. **Given** a real multi-node deployment, **When** the operation sequence is replayed with faults injected, **Then** each Gate guarantee is checked against observable state and any violation is reported with the triggering operation and fault.
2. **Given** a known correctness gap, **When** the harness runs, **Then** the corresponding Aspirational guarantee is reported as tracked-red with its issue link, not as a pass.
3. **Given** the same seed and fault schedule, **When** the harness is re-run, **Then** the result is reproducible (no nondeterministic pass/fail).

---

### User Story 6 - Two-tier verdict, surface smoke, and CI gate (Priority: P3)

A release decision-maker reads one verdict that (a) gates merges on regression of any guarantee that must hold today plus full capability coverage, and (b) presents the known-gap guarantees as a live backlog; each user-facing surface (web, PWA, CLI, MCP) is confirmed to reach the proven core.

**Why this priority**: This turns the proof into an operational instrument — a CI gate plus a readiness dashboard — and extends coverage to the surfaces with lightweight presence checks. It depends on the earlier slices.

**Independent Test**: Run the full instrument in CI; confirm a Gate violation or sub-100% coverage blocks merge, the Aspirational backlog renders with issue links, and each surface has a passing presence check.

**Acceptance Scenarios**:

1. **Given** a change that regresses a Gate guarantee, **When** CI runs, **Then** the merge is blocked and the verdict names the violation.
2. **Given** the current platform, **When** the verdict is produced, **Then** the Aspirational guarantees are listed with pass/fail and issue links as a backlog board.
3. **Given** each user-facing surface, **When** the smoke pass runs, **Then** each surface is confirmed to reach the backend correctly (presence, not deep behavior).
4. **Given** an Aspirational guarantee that has begun to hold, **When** the verdict is produced, **Then** it is flagged for review for promotion to Gate (it is not auto-promoted).

### Edge Cases

- **New capability with no operation**: coverage drops below 100% and the proof fails until an exercising operation is added.
- **Dead guarantee**: a guarantee with no exercising capability is flagged at build time, not silently carried.
- **Aspirational guarantee unexpectedly passes**: flagged for human review for promotion; never auto-promoted to Gate.
- **Gate guarantee flips to failing**: treated as a release-stopper, distinct from an Aspirational failure.
- **Nondeterministic harness result under faults**: treated as a defect in the harness (must be reproducible from seed + fault schedule), not tolerated as flakiness.
- **Surface that cannot reach the backend**: surface smoke fails even if backend correctness passes.
- **Capability marked Deferred/Stub**: registered and mapped to an Aspirational guarantee; excluded from the 100%-coverage gate but shown in the backlog.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST maintain a Capability Registry enumerating every distinct backend capability (deduplicated from the 143 spec folders) and every user-facing surface, each with: originating specs, owning service, surface type, status (Implemented/Stub/Deferred), and links to the guarantees it can affect and the operations that exercise it.
- **FR-002**: The system MUST maintain an Invariant Catalogue of platform guarantees, each expressed as a checkable predicate, tagged Gate or Aspirational, with an issue link for every Aspirational entry, and mapped to the security model it protects (Disclosure/Alteration/Destruction where applicable).
- **FR-003**: The system MUST enforce bidirectional completeness: every spec maps to ≥1 capability; every implemented capability maps to ≥1 operation; every guarantee maps to ≥1 capability that can violate it.
- **FR-004**: The system MUST provide an operation set ("alphabet") where each operation declares the capabilities it exercises and is runnable against both the abstract reference and the real platform.
- **FR-005**: The system MUST provide a deterministic, curated operation sequence that exercises every Implemented capability at least once.
- **FR-006**: The system MUST re-check the entire guarantee set after every operation, against an abstract reference of intended behavior (design correctness).
- **FR-007**: The system MUST fail, and name the violated guarantee plus the triggering operation, whenever any guarantee being checked at that tier does not hold.
- **FR-008**: The system MUST report capability coverage and MUST treat coverage below 100% of Implemented capabilities as a failure of the Gate tier.
- **FR-009**: The system MUST provide a randomized explorer that generates varied operation-and-fault orderings, re-checks guarantees after each step, and emits a minimized, replayable counterexample for any violation, persisting it as a regression seed.
- **FR-010**: The system MUST provide an exhaustive check of the chain-integrity, consensus-safety, and seal-ordering guarantees over a bounded state space including concurrent proposals and message reordering/loss.
- **FR-011**: The system MUST verify that the exhaustive consensus representation and the abstract reference agree on their operation alphabet and guarantee statements (no divergence between the two).
- **FR-012**: The system MUST provide a conformance harness that replays operation sequences against a real multi-node deployment under injected faults (node crash, slow/partitioned peer, concurrent seal, clock advance) and checks guarantees against observable platform state.
- **FR-013**: The conformance harness MUST be reproducible: the same seed and fault schedule MUST produce the same verdict.
- **FR-014**: The system MUST provide a presence-check ("smoke") for every user-facing surface confirming it reaches the backend correctly, without asserting deep surface behavior.
- **FR-015**: The system MUST produce a two-tier verdict: a Gate result (all Gate guarantees hold + 100% Implemented-capability coverage + exhaustive consensus check passes) and a Tracked-red result (each Aspirational guarantee's status with its issue link).
- **FR-016**: The Gate result MUST be usable as a merge-blocking signal; the Tracked-red result MUST be usable as a readiness/backlog dashboard.
- **FR-017**: The system MUST flag (not auto-promote) any Aspirational guarantee that begins to hold, for human review toward Gate promotion.
- **FR-018**: The system MUST register Stub/Deferred capabilities and map them to Aspirational guarantees, excluding them from the 100%-coverage gate while listing them in the backlog.
- **FR-019**: The system MUST be runnable in stages such that the no-infrastructure layers (registry, catalogue, reference proof, randomized explorer) deliver value independently of the real-platform harness.

### Key Entities *(include if feature involves data)*

- **Capability**: a distinct unit of platform functionality. Attributes: id, name, domain, owning service, originating specs, surface type, status, linked guarantees, exercising operations.
- **Invariant (Guarantee)**: a checkable property the platform must uphold. Attributes: id, statement, family, security-model mapping, tier (Gate/Aspirational), issue link, exercising capabilities.
- **Operation**: a unit of behavior in the alphabet (including fault events). Attributes: id, exercised capabilities, applicability to reference vs real platform.
- **Operation Sequence**: an ordered list of operations — either the curated coverage sequence or a generated/minimized exploration sequence.
- **Coverage Ledger**: the record of which capabilities have been exercised in a run; basis for the 100% gate.
- **Verdict**: the two-tier output — Gate (pass/fail + coverage) and Tracked-red (per-Aspirational status + issue links).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the 143 spec folders are traceable to at least one registered capability.
- **SC-002**: 100% of Implemented capabilities are exercised at least once by the curated proof; the proof fails if this drops below 100%.
- **SC-003**: There are zero dead guarantees (no exercising capability) and zero orphan capabilities (no linked guarantee and not marked surface-smoke-only).
- **SC-004**: 100% of guarantees are tagged Gate or Aspirational, and 100% of Aspirational guarantees carry an issue link.
- **SC-005**: The no-infrastructure design-correctness proof completes quickly enough to run on every change (target: under a few minutes on a developer machine).
- **SC-006**: A deliberately injected guarantee violation (mutation) is detected by the proof in 100% of seeded mutation cases — i.e. the proof demonstrably has teeth.
- **SC-007**: The exhaustive consensus check explores its entire bounded state space and reports a definitive pass or a concrete violating trace (no "unknown").
- **SC-008**: Every known correctness gap identified in the v1 assessment is represented by exactly one Aspirational guarantee that reproduces as tracked-red (no known gap silently passes).
- **SC-009**: The conformance harness is reproducible: identical seed + fault schedule yields identical verdicts across repeated runs.
- **SC-010**: Every user-facing surface (web, PWA, CLI, MCP) has a passing presence check.
- **SC-011**: A regression of any Gate guarantee blocks a merge in CI within a single run.

## Assumptions

- **Realization technologies are fixed by design** (held here as constraints, not requirements): an abstract reference model and randomized explorer in the team's primary backend language; an exhaustive check of the consensus core in a formal specification language; a conformance harness using the existing containerized integration-test infrastructure. Detail lives in the design doc.
- **Scenarios may be synthetic**: operation sequences are engineered for coverage, not required to be realistic business workflows.
- **Surface coverage is shallow by intent**: surfaces get presence checks only; deep UI/UX behavior is out of scope.
- **Cadence**: the no-infrastructure layers and the Gate subset run on every change; the full fault-injection matrix runs on a scheduled (e.g. nightly) basis to bound CI cost. (Default; revisit if CI budget allows per-change full runs.)
- **Aspirational promotion is manual**: a passing Aspirational guarantee is flagged for review, never auto-promoted to Gate.
- **The harness phase subsumes the previously deferred cross-node integration harness** (#585 / roadmap Q-03) rather than duplicating it.
- **Capability count is approximate** (~70–90 after dedup); the registry build produces the authoritative list.
- **The existing unit suite and walkthroughs remain**; the oracle sits above them and reuses walkthrough infrastructure in the harness phase. It does not replace them.
- **The oracle does not fix correctness gaps**; it makes them loud, attributable, and gate-able.

## Dependencies

- The 143 spec folders and existing service code (source of the capability dedup and the guarantee statements).
- The existing containerized integration-test infrastructure (basis for the conformance harness).
- The existing walkthrough framework (reused by the harness for happy-path operation drivers).
- Open issues representing known gaps (#119, #585, #787, #814, #917, B-01/B-02 and the stubs) — linked from Aspirational guarantees.
