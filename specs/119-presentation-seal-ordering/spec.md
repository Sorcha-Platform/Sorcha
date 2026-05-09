# Feature Specification: Presentation Lifecycle Chain-Race Resolution via Seal-Aware Ordering

**Feature Branch**: `119-presentation-seal-ordering`
**Created**: 2026-05-08
**Status**: Draft
**Input**: User description: "Feature 119 — Presentation lifecycle chain-race resolution via seal-aware ordering. Closes the two pre-existing chain-integrity race conditions in Feature 111 that prevent the AssuredIdentity Phase 2 walkthrough from completing end-to-end after PR #583 closed the FR-015 advancement gap. The two races affect ~50% of normal-paced production presentation attempts."
**Design document**: `docs/superpowers/specs/2026-05-08-feature-111-chain-races-design.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Fast-citizen presentation completes reliably (Priority: P1)

A citizen approaches a HAIP-gated workflow action (e.g. "verify identity to apply for a driving licence"). The platform shows them a QR code; they scan it with their wallet, approve consent with biometrics, and the wallet posts the verifiable presentation back to the platform. Because this citizen has a saved credential and a fast network, the entire presentation completes in 3-5 seconds — faster than the platform's internal docket-build cycle. The workflow then advances to the next action (e.g. "pay the application fee"), which the citizen submits without delay.

Today: this scenario fails roughly 50% of the time, because internal chain-integrity checks reject either the outcome record or the next action, leaving the citizen with a confusing error and the workflow stuck.

After this feature: this scenario succeeds reliably, regardless of how quickly the citizen completes the presentation.

**Why this priority**: This is the actively-broken path that prevents production rollout of any HAIP-gated workflow. Without this fix, every other production presentation attempt fails. It is also the AssuredIdentity walkthrough Phase 2 success criterion.

**Independent Test**: Run `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway` 10 times consecutively against a fresh local Docker stack. All 10 runs must complete Phase 2 step 7 successfully without retry.

**Acceptance Scenarios**:

1. **Given** a citizen on a HAIP-gated action, **When** they complete the presentation flow in under 5 seconds, **Then** the presentation outcome is recorded on the register and the workflow advances to the next action without manual retry.
2. **Given** a citizen completes a presentation faster than the internal docket-build cycle, **When** they immediately submit the next workflow action, **Then** that next action is accepted on the first attempt with the correct chain linkage.
3. **Given** a citizen takes 20 seconds to complete a presentation (slower than docket-build cycle), **When** the outcome is recorded, **Then** the workflow still advances correctly with no observable difference in behaviour from the fast-citizen path.

---

### User Story 2 — Operators can see and bound never-completing presentations (Priority: P2)

A platform operator is monitoring HAIP-gated workflow health. Occasionally an internal record (e.g. an outcome) may not seal onto the register due to consensus rejection or infrastructure hiccup — historically this manifested as a silent drop. The operator needs to see when this happens, how often, and have the platform fail the affected presentation in a bounded time so it doesn't sit in a half-state forever.

**Why this priority**: Required for safe production rollout — silent failure is unacceptable for a registry platform. Lower priority than US1 because the observed failure rate is low (consensus rejection is rare); the dominant production failure today is the US1 race, not silent never-seals.

**Independent Test**: Inject a forced consensus-rejection on an outcome submission. Verify within the configured TTL (default: matches the presentation validity window, currently 600 seconds): (a) the presentation is moved to a clearly-named failure state, (b) a counter metric increments by exactly one, (c) a structured error log is emitted with the presentation request identifier and predecessor transaction identifier, and (d) operator dashboards reflect the counter increment.

**Acceptance Scenarios**:

1. **Given** an outcome whose predecessor never seals, **When** the configured TTL elapses, **Then** the presentation is marked as failed with a distinct, machine-readable failure reason.
2. **Given** a queue of presentations awaiting seal, **When** an operator queries platform metrics, **Then** they can see the current queue depth per site, the wait-time distribution, and the cumulative failure counter.
3. **Given** an internal seal event was missed (e.g. transient infrastructure hiccup), **When** the recovery sweep runs, **Then** the affected presentation completes within one sweep cycle and a recovery counter increments — no operator intervention required.

---

### User Story 3 — Abandonment records also wait for predecessor seal (Priority: P3)

When a citizen starts a presentation but never completes it, the platform records an abandonment after the presentation's validity window expires. This abandonment record carries a chain pointer to the original presentation start record. Although the race window for this path is much smaller than US1 (the abandonment sweeper fires after the validity window expires, by which time the start record has almost always sealed), the underlying logic has the same defect.

**Why this priority**: Latent. The bug exists today but is rarely triggered because of the timing window. Fixing it consistently with US1 prevents future regressions and avoids a "we fixed two of three identical bugs" trap.

**Independent Test**: Configure a blueprint with an unusually short validity window (e.g. 30 seconds) and `recordAbandonment: true`. Initiate a presentation but do not complete it. Verify the abandonment record seals correctly without chain-integrity errors, regardless of whether the start record was sealed at the time the sweeper fired.

**Acceptance Scenarios**:

1. **Given** a presentation start record still pending seal, **When** the abandonment sweeper fires, **Then** the abandonment record submission waits for the start record's seal before being committed.
2. **Given** a presentation start record already sealed, **When** the abandonment sweeper fires, **Then** the abandonment record is submitted immediately with no observable extra latency.

---

### Edge Cases

- **Service restart with pending seal-waits**: A pending presentation that was queued awaiting seal must survive restart of the workflow service. After restart, recovery must drain the queue without operator intervention or data loss.
- **Internal seal event missed (transient infrastructure)**: The recovery sweep must detect entries that have been waiting longer than expected and complete them via direct register lookup if their predecessor has actually sealed.
- **Predecessor never seals (consensus rejection or stuck)**: Bounded by the presentation TTL. After expiry, the presentation moves to a clearly-named failure state with operator-visible metrics and structured log.
- **Late presentation outcome arrives after abandonment was already recorded**: Existing R6 idempotency guarantee preserved — both records appear on the register with timestamps resolving order; sentinel transitions to `abandoned+outcome`. No queue interaction needed because the start record has long since sealed by the time abandonment is recorded.
- **Concurrent verifier callbacks for the same presentation**: Existing R6 sentinel guard fires before any seal-wait queue interaction — only the first callback proceeds; the rest get an idempotent reply.
- **Duplicate verifier callback while one outcome is queued for seal**: The duplicate observes the new "outcome submission deferred" sentinel state and returns an idempotent reply.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-119-001**: The platform MUST NOT submit a presentation outcome record until the presentation start record it references has been observed sealed in the register.
- **FR-119-002**: The platform MUST NOT submit a presentation abandonment record until the presentation start record it references has been observed sealed in the register.
- **FR-119-003**: The platform MUST NOT advance a workflow instance to its next action on the basis of a presentation outcome until that outcome has been observed sealed in the register.
- **FR-119-004**: When a presentation start record has already sealed at the time an outcome or abandonment is being written, the platform MUST submit the dependent record immediately, without observable extra latency.
- **FR-119-005**: When a presentation outcome has already sealed at the time the workflow advancement is evaluated, the platform MUST advance the workflow immediately.
- **FR-119-006**: The platform MUST detect missed seal events and recover affected presentations within one recovery-sweep cycle (default 5 seconds) without operator intervention.
- **FR-119-007**: The platform MUST fail any presentation whose dependent record has been awaiting predecessor seal for longer than the presentation validity window (default 600 seconds), with a distinct, machine-readable failure reason and an operator-visible structured log.
- **FR-119-008**: The platform MUST emit observability data covering: queue depth per site, wait-time distribution, failure counter, and recovery counter — sufficient for operators to detect both healthy operation and degraded states.
- **FR-119-009**: The platform MUST preserve all existing tamper-evidence properties of the on-register chain — specifically, the rule that each presentation start record has at most one terminal outcome record on the chain.
- **FR-119-010**: The platform MUST be safe under workflow service restart — any presentation that was awaiting predecessor seal at restart time must complete (or fail bounded) without manual intervention or data loss.
- **FR-119-011**: The platform MUST be idempotent under retried verifier callbacks: a duplicate callback for a presentation whose outcome is already queued for seal MUST return the same outcome that the first callback elicited.
- **FR-119-012**: The platform MUST preserve the existing late-presentation-outcome-after-abandonment behaviour: if a verifier callback arrives after the abandonment record has already sealed, the outcome record is still written, and both records remain queryable on the register.
- **FR-119-013**: The verifier-facing callback MUST return success to the verifier within the existing latency budget (i.e. it must not block waiting for predecessor seal). Recording the outcome happens asynchronously after the callback returns.

### Key Entities

- **Presentation start record**: The on-register record that opens a presentation lifecycle. Carries a presentation-request identifier, the workflow instance it relates to, the workflow action it relates to, and the credential requirements digest. Its chain pointer points to the previous workflow step.
- **Presentation outcome record**: The on-register record that terminates a presentation lifecycle with success or decline. Its chain pointer points to the corresponding start record.
- **Presentation abandonment record**: The on-register record that marks a presentation as never-completed after the validity window expired. Its chain pointer points to the corresponding start record.
- **Outcome sentinel**: A short-lived state token (per presentation request) that coordinates which event (outcome callback vs abandonment sweeper vs seal subscriber) has authority to write a record at any given moment. Already exists; gains three new values to cover seal-pending, predecessor-timeout, and validator-rejection states.
- **Seal-wait queue entry**: A durable record that a particular dependent record (outcome, abandonment, or workflow advancement) is waiting for a particular predecessor record to seal before proceeding. Carries the dependent record's full submission payload and the predecessor's identifier.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-119-001**: AssuredIdentity walkthrough Phase 2 completes successfully on 10 of 10 consecutive runs against a fresh local Docker stack.
- **SC-119-002**: For citizens completing presentations in 3 to 30 seconds, the workflow advancement to the next action succeeds on the first attempt 100% of the time (currently fails roughly 50% of the time for fast-citizen flows).
- **SC-119-003**: For citizens completing presentations in under 5 seconds (the formerly-broken path), the time from verifier callback to next-action-ready does not exceed 30 seconds (one or two docket-build cycles).
- **SC-119-004**: For citizens completing presentations in over 30 seconds (the formerly-working path), the time from verifier callback to next-action-ready does not regress by more than 1 second compared to current behaviour.
- **SC-119-005**: When an outcome's predecessor never seals, the platform records the failure within one presentation validity window (default 600 seconds) — never silently and never longer than the window.
- **SC-119-006**: A platform operator inspecting metrics during a clean walkthrough run sees: zero failure counter increments, populated wait-time histogram values for each site, and queue depth returning to zero between presentations.
- **SC-119-007**: Workflow service restart during a queued seal-wait causes zero presentation losses across 5 consecutive restart-while-pending tests.
- **SC-119-008**: No regression in any existing presentation-lifecycle test suite covering outcomes, abandonment, or consumer-agnostic dispatch.
- **SC-119-009**: The on-register one-outcome-per-start-record fork-resistance rule continues to be enforced by the validator: any attempt to attach a second outcome to a start record is rejected.

## Scope and Non-goals

### In scope

- Three workflow-service call sites where chain-pointer-bearing records are submitted or workflow advancements are fired without waiting for predecessor seal.
- A new internal coordination component to maintain seal-wait queues.
- A new internal subscriber that drains those queues on receipt of seal events from the existing event channel, plus a periodic recovery sweep.
- Three new outcome-sentinel states covering the new seal-pending, timeout, and validator-rejection cases.
- New observability surface (metrics, traces, structured logs) on all of the above.
- Test coverage at unit, integration, and walkthrough layers.
- Documentation propagation: the cross-cutting Timebound Presentation Lifecycle pattern in the project's architecture skill, plus the relevant research and data-model sections of the Feature 111 spec.

### Out of scope

- Validator-side tolerance for transactions whose predecessor is still in mempool (rejected as option (B) in the design — bigger blast radius into validator chain invariants for too narrow a problem).
- Decoupling presentation lifecycle records from chain participation entirely (rejected as option (C) for this feature — kept on the table as a clean future migration if the seal-wait latency proves unacceptable in production).
- Any generalised "submit-after-seal" primitive for non-presentation flows. The mechanism stays scoped to presentation lifecycle until a second use case appears.
- Changes to FR-014, FR-015, or FR-017 of the Feature 111 spec.
- Generalised changes to the validator's chain-integrity rules. (A narrow VAL_BP_003 carve-out for `presentation-outcome` and `presentation-abandoned` lifecycle terminals was forced into being during execution after three failed Blueprint-only attempts hit a dead-code path — see `EXECUTION-DEVIATIONS.md` § "Resolution 2026-05-09" and the eight-line change in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`. VAL_CHAIN_001 and VAL_CHAIN_FORK are unchanged. `presentation-initiated` still gets the full reachability check.)
- Any feature flag or in-flight migration. The new behaviour replaces the old code path outright (Feature 111 is master-only and clean-start per its FR-017).

## Assumptions

- The existing `transaction:confirmed` event channel reliably emits one event per sealed transaction with the transaction identifier as a payload field. Multiple existing consumers in the codebase rely on this assumption today.
- A 5-second recovery sweep cadence is sufficient to mask the worst case of a missed event without imposing meaningful operator-visible latency. The figure can be tuned later via configuration if real-world miss rates warrant it.
- The existing presentation validity window default of 600 seconds is the appropriate upper bound for the seal-pending failure timeout. A shorter timeout would risk legitimate slow-seal cases failing; a longer timeout would risk operator-confusion for genuinely stuck records.
- The existing outcome-sentinel mechanism is the right place to record the new seal-pending and failure states. The sentinel is already the single coordination point for which actor (callback vs sweeper) writes which record; extending it to cover seal-deferred submissions is a natural fit.
- The existing idempotency guard on the workflow advancement step is sufficient to handle replays; no additional guard is needed.

## Dependencies

- Feature 111 (Timebound Presentation Lifecycle) — must remain on master with PR #583 (FR-015 advancement) merged.
- The existing `transaction:confirmed` Redis Streams event channel and its publishing path from the Register Service.
- The existing presentation outcome-sentinel mechanism (research R6 in Feature 111 spec).

## Open Questions

None. All design questions resolved during brainstorming on 2026-05-08 (see design document linked in header).
