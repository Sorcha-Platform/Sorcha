# Spec Notes: P2P Register Sync — Brainstorm Backlog

Items discussed during brainstorming (2026-03-28) that are **out of scope** for 071 but should be tracked for future work.

## Deferred to Future Features

### PeerRouter Enhancements

- **Router Connection Pooling** — Currently creates a new gRPC channel per relay message for non-NAT'd recipients. Should reuse channels. Low priority until we have non-NAT'd peers communicating through the Router.
- **Router Rate Limiting / Quotas** — Per-peer relay quota enforcement to prevent abuse. Not needed for trusted local-network scenario but required before public network use.
- **Router Message Compression** — Gzip payload in relay messages for bandwidth reduction on large transaction payloads.
- **Router Relay Acknowledgment Persistence** — Durable event log of all relayed transactions. Currently in-memory only, lost on restart.
- **Full Internet Node** — Deploy a full Sorcha node (not just PeerRouter) on the public internet so at least one peer has a reachable address and can serve as a direct sync source. Would eliminate the NAT relay requirement for most operations.

### Subscription & Discovery Automation

- **Tenant Service → Peer Service Bridge** — Automatic subscription trigger: when an org subscribes to a register via Tenant Service REST API, the Peer Service should automatically begin replication without manual `POST /subscribe`. Could be event-driven (SignalR, Redis pub/sub) or direct HTTP call.
- **Auto-Subscribe on Discovery** — Option for peers to automatically subscribe to newly discovered public registers without operator intervention. Useful for mesh networks but needs governance controls.
- **Subscription Management UI** — Blazor WASM page for viewing available registers, subscribing, monitoring sync progress, and managing subscriptions. CLI-only for 071.

### Replication Improvements

- **Subscription Filters** — Proto defines `RegisterSubscriptionFilters` (transaction_types, participant_ids) but gRPC service returns all transactions. Filtering would reduce bandwidth for peers that only care about specific transaction types.
- **Cross-Register References During Replication** — Verifiable credential chains and cross-register VCs are not replicated in 071. Future feature for multi-register workflows.
- **Incremental Resync** — After prolonged disconnection, only pull the delta (dockets with version > last synced) rather than re-pulling the full chain. The existing code partially supports this via version cursors.

### Consensus & Validation

- **Distributed Consensus / Multi-Validator** (P2P-006) — Beyond single validator. Leader election, BLS12-381 threshold coordination (P2P-004), multi-validator synchronization (P2P-008). Major effort (~76h estimated in MASTER-TASKS).
- **Fork Detection** (P2P-005) — Chain fork handling in Validator Service. Not relevant with single validator but essential for multi-validator.
- **Enclave Support** (P2P-007) — SGX/TDX trusted execution for Validator. Hardware security for validator keys.

### Infrastructure Prerequisites

- **Peer Service PostgreSQL Migration Fix** — Existing approved spec (`docs/superpowers/specs/2026-03-26-peer-persistence-cleanup-design.md`). The `peer.*` schema may not be applied correctly. Should be verified/fixed as part of 071 implementation.
- **PeerRouter Stale Peer Management** — Existing approved spec (`docs/superpowers/specs/2026-03-16-peerrouter-stale-peer-management-design.md`). Two-tier eviction, address dedup. Should be done alongside or before 071 to keep routing table clean.
