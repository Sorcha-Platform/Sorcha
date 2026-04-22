# Phase 0 Research: Agent Persona Mode

All items in Technical Context are resolved. No `NEEDS CLARIFICATION` markers remained in the spec. This document records the deliberate design decisions discovered while grounding the plan in the existing `Sorcha.Agent` codebase.

## R-001: Persona file location and actor-config linkage

**Decision**: Persona is a separate JSON file (`<name>.persona.json`), referenced from the existing actor-config file via a new optional `personaFile` property (relative path, resolved against the actor file's directory).

**Rationale**: Keeps personas swappable without editing actor config; preserves existing one-actor-one-file convention in walkthroughs; matches the brainstorm outcome that personas are composable artifacts. The actor config remains the per-agent file launched by `run-agents.ps1`, so there is no change to manifest discovery or launch sequencing.

**Alternatives considered**:
- Persona inline in actor config (`"persona": { ... }`). Rejected: mixes reactive + initiating concerns in one record and prevents sharing a persona across agents or walkthroughs.
- Persona discovered by filesystem convention (`<actor-name>.persona.json` next to actor file). Rejected: implicit discovery hides intent and breaks when walkthrough authors want to swap personas per run.

## R-002: Instance creation responsibility

**Decision**: Blueprint-instance creation stays in `run-agents.ps1` (or equivalent setup script). The persona submits an action against an already-created instance whose ID is resolved from `state.json` via the existing `VariableResolver`.

**Rationale**: Mirrors the current pattern in `walkthroughs/TradeFinance/run-agents.ps1` (lines 76–107) where the script creates instances before launching agents. Personas become purely about "submit action N on instance X with payload P", which is the same contract the existing `ActionExecutor` already honours for reactive actions. No new blueprint-service code path required.

**Alternatives considered**:
- Persona creates the instance at fire time. Rejected: introduces a second creation code path, duplicates logic already in walkthrough scripts, and complicates multi-agent scenarios where several agents submit against the same pre-created instance.

## R-003: Reactive + persona coexistence (loop model)

**Decision**: Persona runs as an independent `Task` launched from `RunCommand.ExecuteAsync` after authentication succeeds. The existing `await foreach` over `CompositeInboxListener.ListenAsync` continues on the main flow. A shared `CancellationToken` stops both on process shutdown. Neither loop awaits the other.

**Rationale**: The existing main loop in `RunCommand.cs` is single-threaded per agent but fully async; launching the persona loop as a peer `Task` avoids restructuring the main loop, reuses the same HttpClient (HttpClient is thread-safe for concurrent requests), and lets the reactive loop continue even when the persona loop blocks on its interval timer. Satisfies FR-011 (neither blocks the other) and SC-004 (≤25% reactive latency regression).

**Alternatives considered**:
- Interleave persona fires with inbox polls on a single loop. Rejected: couples persona cadence to inbox cadence and starves either side when the other is busy.
- Run persona in a separate hosted service / background worker. Rejected: over-engineers a small feature; the agent has no existing hosted-service container and adding one for this would violate YAGNI.

## R-004: Payload token vocabulary and resolution

**Decision**: Six tokens, resolved fresh on each fire: `${now}`, `${uuid}`, `${counter}`, `${random.int(min, max)}`, `${random.decimal(min, max, precision)}`, `${random.choice([…])}`. Tokens evaluated by a dedicated `PayloadTokenResolver` that walks the template `JsonNode` tree. Unknown functions or malformed arguments fail at persona load time (not at fire time), so bad templates cannot reach the Blueprint Service.

**Rationale**: This exact vocabulary was agreed in the brainstorm and covers every use case named ("invoice value in range", "fresh UUID per fire", "counter for instance references"). Evaluating at load time means scenario authors see errors before the agent starts submitting, which is dramatically easier to debug than silent `${typo}` strings landing in register data (FR-010).

**Alternatives considered**:
- Full templating engine (Handlebars / Liquid / Scriban). Rejected: full sub-language to learn, debug, and secure; overkill for six operations.
- Deferred validation (fail at first fire). Rejected: hides template errors until a scheduled moment, often mid-demo.
- `Random` implementation: use `System.Random.Shared` for non-test code; unit tests inject a seeded `Random` via a narrow `IRandomSource` seam to keep `${random.*}` tests deterministic.

## R-005: Stop-condition semantics

**Decision**: For interval triggers, persona stops when the first of these becomes true: `maxIterations` reached, `until` wall-clock timestamp passed, agent `CancellationToken` signalled. Missing both `maxIterations` and `until` is **not** an error — the persona runs until the agent process stops. Authors who want a bounded run declare at least one.

**Rationale**: Matches the brainstorm outcome (either or both). Allowing neither keeps the "run forever until killed" scenario expressible without adding a third stop mode. Scenario tests always use `maxIterations` for determinism (SC-003).

**Alternatives considered**:
- Require at least one stop condition. Rejected: adds an arbitrary validation rule for a scenario (open-ended soak runs) that is legitimate for load testing.
- Introduce a third `maxDuration` stop condition. Rejected: redundant with `until`; authors can compute `until = now + duration` in the file if they want relative semantics.

## R-006: Failure handling and back-pressure

**Decision**: On persona submission failure (HTTP error, validation reject, rate-limit, transient network): log at `Warning` with full context; do **not** increment the iteration counter; sleep the declared interval (not a retry backoff) before the next attempt; never crash the reactive loop. On non-transient errors repeated three times in a row, log at `Error` and exit the persona loop (reactive loop continues).

**Rationale**: Honours FR-015 (don't lose iteration counter on transient error) and FR-011 (don't abort reactive loop on persona failure). Three consecutive hard failures is a reasonable give-up threshold that prevents a misconfigured persona from hammering a broken endpoint forever while still tolerating a short outage.

**Alternatives considered**:
- Exponential backoff. Rejected: persona already has a declared interval that acts as natural pacing; adding backoff layered on top complicates reasoning about "when did my 20th invoice arrive".
- Stop persona on first failure. Rejected: too brittle for a scenario tool that has to survive a Blueprint Service restart.

## R-007: Observability

**Decision**: Each persona fire emits a structured log entry (`ILogger<PersonaHost>`) with fields: `PersonaName`, `Trigger`, `Iteration`, `BlueprintId`, `ActionName`, `Outcome` (`Submitted`/`Failed`/`Stopped`), `DurationMs`. Load-time validation failures emit `Error`-level logs with the offending token or path. Uses the agent's existing audit-log file when declared (`definition.Logging.ActionLog`).

**Rationale**: Matches Constitution Principle VIII (observability by default, structured logging, no string interpolation). Reuses the existing `AuditLogger` pattern so persona fires appear in the same `*.jsonl` stream as reactive decisions, enabling a single grep for "what did this agent do".

## R-008: Testing strategy

**Decision**:
- **Unit tests (xUnit + FluentAssertions + Moq)** for every new class.
  - `PayloadTokenResolver` tested with seeded `IRandomSource` for determinism.
  - `OnceTriggerLoop` asserts single fire then completion.
  - `IntervalTriggerLoop` tested with a fake clock (`TimeProvider`) — fire at `t=0`, `t=interval`, etc. — and asserts that both `maxIterations` and `until` stop the loop at the right moment.
  - `PersonaSchemaValidator` rejects `${randm.int(...)}`, `${random.int()}`, `${random.choice([])}`, etc.
- **Integration test** in `PersonaHostIntegrationTests` spins up `RunCommand.ExecuteAsync` against a mocked `HttpMessageHandler` that stands in for Blueprint Service. Asserts one-shot persona produces exactly one POST to `/api/instances/.../execute` with the resolved payload.
- **Regression test**: an existing actor config with no `personaFile` field loads and runs identically (FR-012 / SC-005) — covered by an existing-config-shape regression unit test.

**Rationale**: Achieves ≥85% coverage without requiring a live Blueprint Service and keeps the test suite deterministic per Constitution Principle IV.

**Alternatives considered**:
- End-to-end test against docker-compose stack. Rejected for v1: slow, flaky in CI, and the integration test covers the wiring. An e2e check happens organically when the TradeFinance walkthrough runs.
