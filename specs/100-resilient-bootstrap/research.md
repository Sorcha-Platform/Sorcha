# Research: Resilient System Register Bootstrap

**Feature**: 100-resilient-bootstrap
**Date**: 2026-04-11

## Decision 1: Bootstrap Mode Configuration Pattern

**Decision**: Use a `BootstrapMode` enum (`SyncOnly`, `GenesisFile`, `Auto`) on the existing `SystemRegisterOptions` class, bound from `appsettings.json`.

**Rationale**: Follows the existing Sorcha configuration pattern (`IOptions<T>` bound from named sections). The three modes map directly to the three deployment scenarios: joining a network, creating a network, and local development. An enum is validated at bind-time and self-documenting.

**Alternatives considered**:
- Boolean flags (`EnablePeerSync`, `EnableGenesisIngestion`): Rejected — creates invalid combinations (both false) and is harder to reason about.
- Environment variable only: Rejected — inconsistent with the existing `SystemRegister:GenesisFile` config pattern.
- Separate hosted services per mode: Rejected — unnecessary complexity; a single bootstrapper with mode-driven branching is simpler.

## Decision 2: Two-Phase Retry Strategy for SyncOnly Mode

**Decision**: Fast-retry phase (5s interval, 2 minutes duration) followed by backoff-polling phase (5 minute interval, indefinite). All timing configurable.

**Rationale**: 
- **Fast phase**: When a node starts alongside its peers (e.g., `docker-compose up` on a multi-node deployment), peers may take 10-60 seconds to become available. A 5-second poll captures this quickly.
- **Backoff phase**: After 2 minutes, if no peer is available, the problem is environmental (network partition, peer not deployed yet). Polling every 5 minutes is sufficient and avoids log/CPU waste.
- The transition point (2 minutes) is conservative enough to avoid premature backoff but short enough to stop fast-phase log noise quickly.

**Alternatives considered**:
- Exponential backoff only (2s, 4s, 8s, 16s, ...): Rejected — exponential backoff reaches multi-minute intervals too quickly (8 retries = ~8.5 minutes), but loses granularity in the critical first 30 seconds.
- Constant interval polling: Rejected — either too fast (wastes resources when peers are down for hours) or too slow (misses peers coming up quickly).
- Signal-based (Register Service notifies Peer Service to sync immediately): Rejected for this feature — adds inter-service coupling. The current "check local store" approach is simpler and works because Peer Service syncs independently.

## Decision 3: Auto Mode Preserves Current Behaviour

**Decision**: `Auto` mode retains the existing 3-retry, exponential backoff (2s/4s/8s), then falls back to genesis file. Default for `docker-compose`.

**Rationale**: Developer experience must not regress. Existing walkthroughs and CI pipelines depend on quick startup. The current 14-second window is fine for local dev where no real peers exist.

**Alternatives considered**:
- Making `SyncOnly` the default: Rejected — breaks all local dev and CI workflows.
- Extending Auto's peer window to 60 seconds: Rejected — delays local dev startup for no benefit (no peers in local mode).

## Decision 4: Idempotent Check Per Retry Iteration

**Decision**: Every retry iteration (regardless of mode) starts by checking if the system register exists locally. If found, bootstrap completes immediately.

**Rationale**: The Peer Service's `RegisterSyncBackgroundService` runs independently and may populate the system register at any time. Checking each iteration ensures the bootstrapper picks this up without additional coordination.

**Alternatives considered**:
- Event-based notification from Peer Service: Rejected — adds coupling between services for a problem that a simple poll solves.
- Only check at startup: Rejected — misses registers synced after the first check.

## Decision 5: Log Frequency Management

**Decision**: One log message per retry attempt. During fast-retry phase, log at `Information` level. During backoff phase, log at `Information` level on first occurrence, then `Debug` for subsequent identical-state messages (still retrying, no change). Phase transitions always logged at `Information`.

**Rationale**: Operators watching startup need to see progress. Once in steady-state polling, repetitive "still waiting" messages are noise at `Information` level. Structured logging with attempt count, elapsed time, and next interval gives operators everything they need per message.

**Alternatives considered**:
- Always log at `Information`: Rejected — creates log noise over hours of polling.
- Suppress all logs during backoff: Rejected — operators can't tell if the service is alive.
- Periodic summary logs (e.g., every 10 retries): Rejected — harder to implement, less predictable timing.

## Decision 6: No Changes to GenesisIngestionService or CLI

**Decision**: `GenesisIngestionService` and `SystemRegisterCommands` (CLI) remain unchanged. All changes are in `SystemRegisterBootstrapper` and `SystemRegisterOptions`.

**Rationale**: The genesis loading, verification, and ingestion logic is correct. The problem is solely in the orchestration layer (bootstrapper) that decides _when_ to use genesis vs. peer sync. Keeping changes narrow reduces risk.
