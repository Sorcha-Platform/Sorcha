# Feature Specification: Agent Persona Mode

**Feature Branch**: `110-agent-persona-mode`
**Created**: 2026-04-22
**Status**: Draft
**Input**: Brainstormed design agreeing on a unified persona mechanism that covers both one-shot starting-action kickoff and recurring scenario data generation, with personas declared as per-agent JSON files composed by the walkthrough scenario manifest.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Unblock Walkthrough Kickoff (Priority: P1)

A walkthrough author runs the TradeFinance (or ConstructionPermit) scenario with its existing agent runner. Today the runner launches every agent, but all agents idle because the first action has no prior transaction to put anything in anyone's inbox. With persona mode, the author declares a one-shot persona for the agent that owns the starting action; when the runner launches agents, that persona fires once, submits the starting action, and exits. Downstream agents pick the flow up through their normal reactive inbox behaviour.

**Why this priority**: This is the blocking defect for current demo and walkthrough infrastructure. Without it, the flagship multi-agent walkthroughs cannot run end-to-end from a single command. It is also the smallest, lowest-risk use of the mechanism, so it validates the design before the recurring case is exercised.

**Independent Test**: Fully testable by running the TradeFinance agent runner end-to-end. Success is observable in register state: instance for action 1 exists, submitted by the persona-equipped agent, within seconds of the runner starting, with no human intervention and no separate sequential kickoff script invoked.

**Acceptance Scenarios**:

1. **Given** a walkthrough scenario manifest where the procurement-mgr agent has a one-shot persona file declared, **When** the agent runner is launched, **Then** exactly one instance of the starting action is created in the register within the persona's configured startup window and the persona loop exits cleanly.
2. **Given** a ConstructionPermit manifest that declares an equivalent one-shot persona for its first-action agent, **When** the runner is launched, **Then** the first action is submitted and the remaining reactive agents progress the workflow without any code change to the agent binary.
3. **Given** a manifest where the agent that owns the starting action has no persona declared, **When** the runner is launched, **Then** behaviour is identical to today: that agent idles waiting on its inbox and the walkthrough does not progress until an external submission occurs.

---

### User Story 2 - Generate Scenario Register Data (Priority: P2)

A scenario author wants a register populated with realistic volume and variation for a demo or load test. They declare a persona with an interval trigger, a maximum iteration count, and payload templating that varies a monetary amount inside a defined range. Launching the agent produces a stream of workflow instances with differing values, spaced by the interval, until the cap is hit or the stop time is reached, at which point the persona loop stops cleanly while the agent continues responding to any reactive work.

**Why this priority**: This is the richer capability the persona mechanism is being built for, but it depends on the P1 machinery being in place. It directly serves demos, screenshots, and any future load or scale testing that wants plausible data rather than hand-seeded fixtures.

**Independent Test**: Testable by running a single agent with a recurring persona against an empty register and asserting the register contains the expected number of instances, each with a payload value inside the declared range, produced across an elapsed time consistent with the configured interval.

**Acceptance Scenarios**:

1. **Given** a persona configured with interval trigger and a maximum iteration count of 20, **When** the agent is launched and left running, **Then** exactly 20 workflow instances are created, each spaced by approximately the configured interval, with payload values drawn from the declared range, and the persona loop reports completion.
2. **Given** a persona configured with interval trigger and an absolute stop timestamp earlier than the iteration cap would be reached, **When** the agent runs until that timestamp, **Then** the persona stops submitting new instances at (or before) the timestamp regardless of remaining iteration budget.
3. **Given** a persona configured with both an iteration cap and a stop timestamp, **When** the agent runs, **Then** the persona stops at whichever limit is hit first.
4. **Given** a running recurring persona, **When** the agent process is terminated, **Then** the persona stops immediately and no further instances are created.

---

### User Story 3 - Coexistence with Reactive Behaviour (Priority: P2)

An agent may have a persona declared and still be expected to respond to inbound actions routed to its wallet (for instance, the agent that initiates a workflow is often also a responder to later actions in that same workflow). The agent must service both responsibilities concurrently without one blocking the other and without duplicate or lost actions.

**Why this priority**: The design commits to personas being additive rather than a separate mode. If the reactive path regresses when a persona is present, the mechanism cannot be adopted by any agent that also participates in downstream actions, which describes the realistic multi-agent walkthroughs.

**Independent Test**: Testable by running an agent with both a persona declared and a reactive action waiting in its inbox. Both the persona-initiated submission and the reactive response must complete within expected time bounds, each exactly once.

**Acceptance Scenarios**:

1. **Given** an agent with a one-shot persona and an unrelated pending action already addressed to its wallet, **When** the agent is launched, **Then** both the persona submission and the reactive response are executed exactly once and the relative timing does not starve either path.
2. **Given** an agent with a recurring persona, **When** an inbound action arrives mid-run, **Then** the reactive response is processed without waiting for the next persona tick, and the persona interval is not skewed by the reactive work.

---

### User Story 4 - Human-Editable Scenario Tuning (Priority: P3)

A non-developer (walkthrough author, demo operator) wants to adjust scenario parameters — interval length, iteration count, value range, counterparty list — without editing agent source code or rebuilding. They open the persona file, change the relevant field, and re-run the scenario.

**Why this priority**: Supports the broader goal of making walkthroughs and scenario registers a self-service surface for people who do not maintain the agent codebase. It is the softest of the user-facing criteria; the feature can ship without full polish here, but persona files must at least be human-readable.

**Independent Test**: Ask a non-developer reviewer to change the interval and value range in a supplied persona file, re-run the scenario, and inspect register state. Success is them completing the task without needing to consult agent source.

**Acceptance Scenarios**:

1. **Given** a persona file with an interval of 60 seconds and value range £100–£1,000, **When** an author edits only those fields and re-runs, **Then** the next run uses the new interval and range with no other behaviour changes.

---

### Edge Cases

- **Payload template refers to a token that does not exist** (e.g. typo `${randm.int(...)}`). The persona must fail fast at load time with a clear error rather than silently emitting literal `${...}` into register data.
- **`until` timestamp is already in the past at agent startup.** The persona must not fire at all; it must log and exit cleanly.
- **`once` trigger with a non-reachable starting action** (blueprint ID unknown, action name mismatches blueprint, agent wallet not authorised to submit). The persona must surface the failure visibly and not retry indefinitely; reactive behaviour must remain unaffected.
- **Persona fires faster than the target service can accept submissions** (rate limit, transient outage). The persona must respect the service's back-pressure signal and must not erase the iteration counter on transient failures.
- **Two personas on two different agents declare the same starting action.** Both fire; two instances are created. This is intentional — de-duplication is the scenario author's responsibility, not the runtime's.
- **Manifest references a persona file that does not exist or cannot be parsed.** The agent must refuse to start (or start in reactive-only mode with a clear warning, per the configured policy) rather than silently ignoring the persona.
- **Agent clock skew relative to `until` timestamp.** The persona treats `until` as wall-clock on the agent host; skew is the operator's problem to understand. This is acceptable because persona mode is for demo/scenario use, not consensus-critical work.
- **Recurring persona interrupted mid-fire by process shutdown.** No guarantee of at-least-once or exactly-once across restarts; on restart the iteration counter resets (v1 is stateless across process lifetimes).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A persona MUST be declared in its own file, separate from blueprint and agent source, in a human-readable JSON format.
- **FR-002**: A walkthrough scenario manifest MUST be able to associate an agent with at most one persona file; manifests with no persona reference for an agent MUST produce today's reactive-only behaviour.
- **FR-003**: The persona file MUST declare a trigger of exactly one of two kinds in v1: `once` or `interval`.
- **FR-004**: A `once` trigger MUST fire exactly one submission after the agent starts and the persona loop MUST then terminate.
- **FR-005**: An `interval` trigger MUST accept a duration (seconds or minutes granularity) and MUST fire repeated submissions at approximately that cadence subject to declared stop conditions.
- **FR-006**: An `interval` trigger MUST support an optional `maxIterations` cap and an optional `until` absolute timestamp; when both are declared the persona MUST stop at whichever limit is reached first.
- **FR-007**: Killing the agent process MUST immediately stop any persona loop; the persona MUST NOT be resumable across process lifetimes in v1 (iteration state is not persisted).
- **FR-008**: The persona MUST declare the target of its submission: which blueprint and which action within that blueprint serve as the entry point.
- **FR-009**: The persona MUST declare a JSON payload template that is submitted at each fire; the template MUST support the following substitution tokens, resolved freshly at each fire: current timestamp in ISO 8601 form, fresh UUID, monotonically increasing iteration counter starting at 1, uniformly random integer in inclusive range, uniformly random decimal with declared precision, and uniform random choice from a literal list.
- **FR-010**: Payload tokens that reference an unknown function or have malformed arguments MUST cause the persona to fail at load time with an error identifying the offending token; the persona MUST NOT emit unresolved `${...}` text into register data.
- **FR-011**: An agent with a persona declared MUST continue to service its reactive inbox loop concurrently with its persona loop; neither loop may block the other indefinitely.
- **FR-012**: An agent without a persona declared MUST behave identically to its current (pre-feature) behaviour.
- **FR-013**: A persona submission MUST use the same wallet identity, authentication, and service clients as the agent's reactive submissions; there MUST NOT be a separate persona-only identity or auth path.
- **FR-014**: When a persona is configured but cannot be loaded (file missing, malformed, references a missing blueprint or action, references tokens the runtime cannot resolve), the agent MUST surface the failure loudly before the persona's first scheduled fire and MUST NOT silently degrade to reactive-only without an operator-visible signal.
- **FR-015**: When a persona submission fails at runtime (service error, rate limit, validation rejection), the persona MUST NOT advance its iteration counter for that failure, MUST respect back-pressure rather than tight-looping, and MUST NOT abort the reactive inbox loop.
- **FR-016**: The feature MUST be strictly additive: no existing agent, blueprint, walkthrough script, or manifest schema that does not opt in MUST change in behaviour or required configuration.

### Key Entities

- **Persona**: A declarative description of autonomous initiating behaviour for a single agent. Carries a trigger (kind + timing), optional stop conditions, a target blueprint and entry action, and a payload template. Lives in its own file, keyed by the manifest.
- **Scenario Manifest**: The walkthrough-level definition already responsible for saying which agents run against which blueprint with which wallets. Grows an optional per-agent persona reference. Remains the single place where a scenario is composed.
- **Trigger**: The when-to-fire half of a persona. `once` fires a single submission at agent start. `interval` fires repeatedly at a declared cadence, bounded by optional `maxIterations` and/or `until`.
- **Payload Template**: A JSON shape with optional substitution tokens that are evaluated fresh at each fire, producing the concrete payload submitted for the entry action.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Running the TradeFinance walkthrough agent runner with a one-shot persona for its starting-action agent produces a submitted starting action within 30 seconds of runner start, with no manual kickoff step, measured over five consecutive clean runs with zero exceptions.
- **SC-002**: The ConstructionPermit walkthrough can be unblocked end-to-end by adding a single persona file and one manifest reference, with no changes to the agent binary or any blueprint, demonstrating the mechanism generalises.
- **SC-003**: A recurring persona declared with an iteration cap of 20, an interval of 30 seconds, and a random decimal payload value in a declared range produces exactly 20 register instances whose payload values all fall within the declared range, across an elapsed time between 9 and 12 minutes, measured over three consecutive runs.
- **SC-004**: An agent with a persona, placed in a scenario that also routes at least one reactive action to that agent, processes both the persona submission and the reactive response exactly once, with the reactive response latency no worse than a no-persona baseline by more than 25%.
- **SC-005**: Every existing walkthrough and agent that does not opt into persona mode passes its current test and smoke-run suite unchanged after the feature ships, with zero regressions attributable to persona infrastructure.
- **SC-006**: A reviewer previously unfamiliar with the agent codebase can edit a supplied persona file to change interval, iteration count, and value range, and re-run the scenario to observe the new behaviour, in under 10 minutes without reading agent source code.

## Assumptions

- Walkthrough scenarios are not held to consensus-grade timing guarantees; wall-clock-driven triggers and host-local state are acceptable for v1.
- Exactly-once or at-least-once semantics across process restarts are out of scope for v1; iteration state is in-memory only.
- The agent's existing service clients, wallet auth, and submission path are sufficient for persona submissions with no auth or rate-limit redesign required.
- Humans writing persona files are comfortable with JSON and a very small token vocabulary; no GUI editor is required in v1.
- A single persona per agent is sufficient to unblock both walkthrough kickoff and scenario data generation as currently envisioned; multi-persona composition can be added later without reshaping the file or manifest.
- Scenario authors accept responsibility for avoiding duplicate kickoffs across agents targeting the same starting action; the runtime does not de-duplicate.
