---
run_timestamp: 2026-04-20-initial
peer_a_version: unreleased (Feature 107 PR 4 baseline)
peer_b_version: unreleased (Feature 107 PR 4 baseline)
outcome: env-failure
---

# Cross-peer smoke findings — initial baseline (Feature 107 PR 4)

## Topology

- Peer A: `docker-compose.yml` default stack (API Gateway on `:80`).
- Peer B: `docker-compose.federation.yml` overlay on the same host.
  - API Gateway on `:8081`.
  - Peer-scoped Redis / Postgres / Mongo so state is independent.
  - Peer discovery configured via `PeerService__SeedNodes__SeedNodes__1__*`
    on both peers pointing at each other's peer-service container.
- Shared Docker network: `sorcha-network`.

## Timings

- **compose-up**: n/a (not yet exercised)
- **peers-healthy**: n/a
- **register-native-delivery**: n/a
- **holder-accept-roundtrip**: n/a

## Anomalies

- **Initial baseline — not yet exercised end-to-end.** PR 4 publishes the
  compose overlay + runner + findings format as measurement tooling
  per spec FR-039 ("MUST NOT block the feature's release on a failure
  or anomaly — its purpose is measurement, not gating").
- Steps 3–5 in `run-multi-peer.ps1` are operator-completion tasks.
  First operator run captures real timings and replaces this baseline
  with a proper `pass` / `degraded-pass` / `fail` finding.
- Known concerns for the first operator run:
  - Blueprint Service startup on peer B needs EF migrations applied
    (same pending-model-changes issue PR 1 hit; `docker compose build`
    first).
  - Peer discovery may take several poll cycles before peer-a sees
    peer-b in its topology. `peer:topology` Redis channel is the
    best probe for readiness before issuing the register.
  - Register creation on peer A must propagate to peer B before the
    citizen on peer B can subscribe. If it doesn't, subscribe-by-id
    will 404 — retry with backoff.

## Reproduction

```
docker compose -f docker-compose.yml -f docker-compose.federation.yml up -d
pwsh walkthroughs/AssuredIdentity/run-multi-peer.ps1
```

## Outcome rationale

This run is the initial baseline committed alongside the PR 4 tooling.
The smoke test infrastructure (compose overlay + runner + findings
format) is in place; steps 3–5 of the runner require operator completion
to produce the first real measurement. Per FR-039 the smoke is
non-blocking — Feature 107 ships with this baseline in place and the
first operator-driven run replaces it.

## Follow-up

- Operator completion of `run-multi-peer.ps1` steps 3–5 tracked as issue
  (to be filed once baseline is established on an operator's hardware).
- Cross-peer credential-delivery regressions surfaced by future runs
  belong against the peer-replication subsystem (Feature 106), not this
  feature.
