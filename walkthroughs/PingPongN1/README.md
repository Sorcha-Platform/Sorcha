# PingPongN1

Two orgs on two machines, one public register, ping-pong messaging over P2P.

| Side  | Host                 | Org       | Participant | Role                |
|-------|----------------------|-----------|-------------|---------------------|
| Local | Docker (`Phaethon`)  | Ping Labs | Ping        | Subscriber (NAT'd)  |
| n1    | Azure VM (`n1.sorcha.dev`) | Pong Corp | Pong  | Register owner (public) |

The register lives on n1 because local is behind NAT — putting the register on the public side means pulls run in the direction that works today.

```
                  subscribe (FullReplica)            submit action 1
                  ◀──────────────────────              ◀──────────
Pong Corp (n1) ──── advertises ────▶ Ping Labs (local) ──── signed tx ──▶ n1
                  ──── pulls dockets ────▶
                   (incl. action 0 from Pong)
```

## What's wired and passing today

Run with:

```powershell
pwsh walkthroughs/PingPongN1/setup.ps1   # one-shot: provision orgs + wallets on both sides
pwsh walkthroughs/PingPongN1/run.ps1     # cross-machine probe with specific tx-id checks
```

Infrastructure steps 1–7 all pass:

1. Log in as platform admin on both nodes, switch into Ping Labs (local) and Pong Corp (n1).
2. Pong Corp creates a public register on n1 and flips `advertise=true`.
3. Local's peer-service heartbeat picks up the advertisement; Ping Labs subscribes (FullReplica).
4. n1 → local genesis docket pulls through (validated directly against local's `/api/registers/{id}`).
5. Pong Corp publishes the ping-pong blueprint on n1 with both wallets mapped.
6. Each side publishes its own participant identity on the register — both transactions land where they were submitted.
7. Pong Corp creates a blueprint instance on n1.

## What stops the round-trip today — two related findings

The runner executes action 0 on n1 successfully, but the round-trip currently halts there. Two real platform gaps surface:

### Finding A — Register owner's peer-service cache is empty for its own register

When Pong Corp's validator-service seals a docket on n1, it writes to the local **register-service MongoDB**, but **not to the local peer-service `RegisterCache`**. So when local's `RegisterReplicationService.PullFullReplicaAsync` calls `PullDocketTransactions` on n1, n1's `RegisterSyncGrpcService` (backed by `RegisterCache`) throws `NotFound` — even though n1 genuinely owns the register.

Observable symptom on local's peer-service logs:

```
ERR  Error syncing register 728e…3bb from peer n1.sorcha.dev
     Grpc.Core.RpcException: Status(StatusCode="NotFound",
       Detail="Register '728e66cdfb9e400d97d26d25c7b1c3bb' not found in local cache")
```

Genesis docket 0 does come through because it's pushed on seal via a different path (`POST /api/registers/{id}/dockets` from validator → register-service, which the subscriber fetches via REST), but subsequent dockets only exist in register-service and aren't served back out of the peer-service.

**Fix shape:** Either
- have validator-service (or register-service) also write each sealed docket into the local `RegisterCache` entry when it is the register owner (`RegisterCacheEntry.AddDocket/AddTransaction`), **or**
- have `RegisterSyncGrpcService.SyncDockets`/`PullDocketTransactions` fall back to `IRegisterRepository` / `ITransactionRepository` when the cache misses on a register the local node actually owns.

The second option is cleaner architecturally (cache stays a cache, repository stays authoritative).

### Finding B — No push-on-seal for the reverse (NAT'd) direction

Once local submits action 1, the transaction lands on local's register. For it to appear on n1, **n1 would need to dial local's peer gRPC port** — but local is behind NAT, so n1 can't. With the `n0.sorcha.dev` PeerRouter retired, the current code has no fallback: `TransactionDistributionService` is plumbed but unwired (no caller invokes `DistributeTransactionAsync` on docket seal), and `RegisterReplicationService.PullFullReplicaAsync` on n1 has no channel to pull from.

Local still holds an open outbound gRPC channel to n1 (heartbeats use it). A push-on-seal worker on local could reuse that same channel to hand dockets to n1, closing the loop without any relay.

**Fix shape:**
- Subscribe to the existing `docket:confirmed` Redis event (same hook the `InstanceMirrorReconstructor` uses) in a new `DocketPushService` hosted on the peer-service.
- On event, iterate the connected peer channels (`PeerConnectionPool.GetAllActiveChannels()`), filter to peers that `_peerListManager` marks as having subscribed to the register, and call a new `DocketSync.PushDocket(registerId, docketData, txData[])` RPC on each.
- Server side: `DocketSyncGrpcService.PushDocket` validates and hands the docket to register-service's normal ingest path (`POST /api/registers/{id}/dockets` equivalent).
- Persist a per-subscriber bookmark (e.g. `LastPushedDocketVersion` on `RegisterSubscription`) so restart doesn't re-push the world.

This also closes half of Finding A — subscribers would no longer need to pull at all for registers whose owner pushes.

## Files

| File | Purpose |
|------|---------|
| `blueprints/ping-pong.json` | Two-action looping blueprint, starts on `pong` (n1) so the register-owner initiates. |
| `setup.ps1` | Provisions Ping Labs (local) + Pong Corp (n1), one wallet each. Idempotent. |
| `run.ps1` | Drives the flow with per-step verification and a round-by-round outcome table. |
| `state.json` | Generated by setup.ps1 — shared IDs + URLs for run.ps1. |

## Prerequisites

- Local Docker stack running (`docker compose up -d`) with peer-service running PR #353 (self-introduce on bootstrap).
- n1.sorcha.dev reachable, running the same PR #353 image (post Docker Publish).
- Local `.env` seeded at n1:
  ```
  SEED_PEER_NODE_ID=n1.sorcha.dev
  SEED_PEER_HOST=n1.sorcha.dev
  SEED_PEER_PORT=50051
  SEED_PEER_ENABLE_TLS=true
  ```
- Platform seed admin (`admin@sorcha.local` / `Dev_Pass_2025!`) on both machines.

## Expected output

```
  Infra (steps 1-7): 7/7 passed

  Round  Pong-n1-sent  Pong→local  Ping-local-sent  Ping→n1
    1         ✓          ✗            ✗           ✗

  Findings:
    • Round 1: action-0 tx <id> not pulled to local within 120s
    • Round 1: InstanceMirrorReconstructor did not materialise instance <id> on local
  RESULT: PARTIAL (infra PASS, see findings)
```

PASS is gated on the two findings above being fixed. The runner is intentionally strict — it verifies each specific transaction id cross-visibly rather than just counting transactions, so a future fix to either gap will flip rounds to green without any script changes.
