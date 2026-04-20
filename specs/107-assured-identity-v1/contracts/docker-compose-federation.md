# Contract: docker-compose.federation.yml topology

**Feature**: 107-assured-identity-v1
**File location**: repository root, alongside existing `docker-compose.yml`

## Purpose

A two-peer Sorcha federation that supports `walkthroughs/AssuredIdentity/run-multi-peer.ps1` exercising the cross-peer credential delivery path of Feature 106. Two complete peer stacks subscribed to a shared register.

## Topology

```
┌─────────────────────────────┐         ┌─────────────────────────────┐
│         Peer A              │         │         Peer B              │
│  ─────────────────          │         │  ─────────────────          │
│  api-gateway-a (port 8081)  │         │  api-gateway-b (port 8082)  │
│  blueprint-svc-a            │         │  blueprint-svc-b            │
│  register-svc-a             │  ◄───►  │  register-svc-b             │
│  validator-svc-a            │ peer-svc│  validator-svc-b            │
│  wallet-svc-a               │  gRPC   │  wallet-svc-b               │
│  tenant-svc-a               │         │  tenant-svc-b               │
│  haip-svc-a                 │         │  haip-svc-b                 │
│  peer-svc-a                 │         │  peer-svc-b                 │
│  postgres-a (per service)   │         │  postgres-b (per service)   │
│  mongo-a                    │         │  mongo-b                    │
│  redis-a                    │         │  redis-b                    │
└─────────────────────────────┘         └─────────────────────────────┘
                  │                                    │
                  └────────── shared docker network ───┘
                              (sorcha-federation)
```

Both peers run identical service images. The only differences:
- Hostnames are suffixed `-a` / `-b`
- API gateway ports differ (8081 / 8082) so a host browser can hit either
- Each peer has its own database stack (no shared DBs)
- Each peer's `Peer:Seeds` configuration includes the other peer's `peer-svc` endpoint, so peer discovery is bootstrapped at startup

## Service composition

The compose file imports the existing `docker-compose.yml` service definitions where possible (via `extends:`) and parameterises hostnames and ports per peer. New compose-only resources:

- `sorcha-federation` Docker network (bridge)
- Per-peer named volumes for postgres / mongo data (so peer state persists across `docker compose down` for forensics)
- Per-peer environment overrides for `Peer:Seeds`, `Peer:NodeId`, ports

## Configuration overrides

Each peer's `appsettings` overrides include:

```jsonc
{
  "Peer": {
    "NodeId": "peer-a",                      // or peer-b
    "Seeds": [ "peer-svc-b:5002" ]           // or peer-svc-a:5002
  },
  "Cluster": {
    "FederationMode": true                   // enables cross-peer replication paths
  }
}
```

## Setup behaviour

`run-multi-peer.ps1` performs setup in this order:

1. `docker compose -f docker-compose.federation.yml down -v` (clean slate)
2. `docker compose -f docker-compose.federation.yml up -d` (start both peers)
3. Wait for both API gateways to report healthy (poll `/health` endpoints)
4. Provision the Government org on **peer A only** (the issuer)
5. Provision the citizen public-org account on **peer B** (the holder)
6. Citizen subscribes to peer A's Assured Identity register (using existing peer-discovery + register-subscription path)
7. Wait for peer B to confirm subscription is replicated (poll register subscription state)
8. Run Phase 1 submission on peer B (citizen submits to their own peer)
9. Wait for the action to replicate to peer A (via existing register replication)
10. Government agent on peer A picks up the assessor action, approves
11. Credential is sealed as a recipient-addressed disclosure (Feature 106 register-native delivery)
12. Wait for peer B's `InboundCredentialDetector` to surface the credential in the holder's MyCredentials PENDING tab
13. Holder Accepts on peer B
14. Accept transaction is observed on peer A (closes the loop)
15. All milestones recorded; findings document written

## Resource budget

Two full Sorcha stacks. Each stack carries roughly the same resource cost as `docker-compose.yml`:

- ~16 service containers per peer (32 total)
- ~3 databases per peer (6 total: 2 postgres, 2 mongo, 2 redis)
- Memory: estimate ~6–8 GB total under load
- Startup time: ~60–90 seconds for both peers to be healthy

This is a smoke test, not a continuous environment. Brought up for a single run, torn down after.

## Cleanup

`run-multi-peer.ps1` ends with:

```powershell
if (-not $KeepRunning) {
  docker compose -f docker-compose.federation.yml down -v
}
```

The `-KeepRunning` switch leaves both peers up so a developer can inspect them after a failed run.

## Compatibility with existing docker-compose.yml

The existing `docker-compose.yml` is unchanged. Single-peer demo (`run.ps1`) uses the existing compose file. Only `run-multi-peer.ps1` uses the new federation compose file. The two are mutually exclusive (you would not run both at the same time on one host).

## Acceptance

- `docker compose -f docker-compose.federation.yml up -d` brings both peer stacks healthy within ~90 seconds on reference hardware
- Both peers' API gateways respond at distinct host ports (8081 / 8082)
- A register created on peer A is observable via subscription on peer B within ~30 seconds
- A credential issued on peer A reaches peer B's MyCredentials PENDING tab within the SC-009 latency budget under normal conditions
- `down -v` cleanly removes all containers, volumes, and the federation network
