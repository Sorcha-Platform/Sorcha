# Quickstart — Register State Aggregation & Local Relationship

**Feature**: `108-register-local-relationship`

This is the shortest viable verification path for the feature end-to-end. It assumes a working local Sorcha dev stack (Docker) and `n1.sorcha.dev` reachable.

---

## Prerequisites

- Local Docker stack up: `docker compose up -d` — peer-service, register-service, validator-service, wallet-service, tenant-service, api-gateway, blueprint-service all Running.
- `n1.sorcha.dev` up, running the latest platform image (must include PR #357 for the forward-direction roster-extraction auth fix).
- Local `.env` configured with seed peer pointing at n1:
  ```env
  SEED_PEER_NODE_ID=n1.sorcha.dev
  SEED_PEER_HOST=n1.sorcha.dev
  SEED_PEER_PORT=50051
  SEED_PEER_ENABLE_TLS=true
  ```
- Platform seed admin `admin@sorcha.local` / `Dev_Pass_2025!` on both nodes.

---

## Happy-path verification (SC-001 — PingPongN1 flips to PASS)

```powershell
# One-shot setup (idempotent): orgs, wallets, seed peer subscription
pwsh walkthroughs/PingPongN1/setup.ps1 -Force

# Two full round-trips
pwsh walkthroughs/PingPongN1/run.ps1 -Rounds 2
```

**Expected output** (after this feature lands):

```
Round  Pong-n1-sent  Pong->local  Ping-local-sent  Ping->n1
  1         ✓            ✓             ✓              ✓
  2         ✓            ✓             ✓              ✓
RESULT: PASS
```

Exit code `0` indicates full round-trip success. A PARTIAL result (exit 2) means one of the four legs failed — inspect the findings list and cross-reference with the per-axis debug steps below.

---

## Per-axis debug steps

### Relationship derivation (Story 2)

```bash
# Against the local node, query the derived relationship for a register
curl -H "Authorization: Bearer $LOCAL_SERVICE_JWT" \
  http://localhost/api/registers/{registerId}/local-relationship | jq
```

Expected shape:
```json
{
  "registerId": "…",
  "roles": ["Validator"],
  "controlRecordVersion": 0,
  "isOwner": false, "isValidator": true, "isSubscriber": false, "derivedAt": "…"
}
```

On the register owner, the same call should return `"roles": ["Owner", "Validator"]` (or just `Owner` if genesis has separate validator keys). On a plain subscriber, `"roles": []` with `"isSubscriber": true`.

### Sync state (Story 3)

```bash
curl -H "Authorization: Bearer $LOCAL_SERVICE_JWT" \
  http://localhost/api/registers/{registerId}/sync-state | jq
```

Expected in steady state: `"state": "CaughtUp"`, `distinctPeerObservers ≥ 1`, `localHeight == networkHeightHighWaterMark`, `lastAdvertAt` within 60s of now.

To provoke `Syncing`: stop the owner's register advertising, wait, restart — the subscriber should transition `CaughtUp → Syncing → CaughtUp` as the pull catches up.

To provoke `Indeterminate`: block heartbeat traffic from n1 → local for 60+ seconds; subscriber's state should degrade.

### Validator enrolment (Story 4)

```bash
# On the subscriber (local), the validator's monitoring list should NOT include
# the remotely-owned register.
curl -H "Authorization: Bearer $LOCAL_SERVICE_JWT" \
  http://localhost/api/validator/monitored-registers | jq
```

Expected: only registers where the local node's validator key appears on the roster. For PingPongN1, the subscriber side should show an empty list (subscriber isn't on n1's roster).

Negative check: post a direct submission to the subscriber's validator for a register it doesn't validate:

```bash
curl -X POST -H "Authorization: Bearer $LOCAL_SERVICE_JWT" \
  -H "Content-Type: application/json" \
  -d @subscribed-register-submission.json \
  http://localhost/api/v1/transactions/validate
```

Then confirm no new docket appears on either node for that submission — only the owner seals it (verified by comparing the owner's register height before and after).

### Submission fan-out (Story 5)

Inspect a Blueprint.Service log line for an action on a subscribed register — you should see two outbound calls:
- `Submitting action transaction … to Validator Service`
- `Distributing action transaction … to N peer(s)`

… and no conditional branching on register ownership in the `ActionExecutionService` source (grep check). The peer-distribute call is the path that reaches the owner.

---

## Rollback / regression checks

- PR #357 auth fix behaviour preserved: peer-service logs should show zero `Failed to read docket … Unauthorized` warnings during normal operation (SC-008).
- Legacy pre-086 registers still finalize dockets via the proposer-signature fallback path (FR-020 + the existing `ExtractFromGenesisDocket` legacy path).
- Operator dashboard sync-state column shows the new enum values with a display-name map (string "Caught-up" → enum `CaughtUp` → label "Caught up").

---

## Build & test

```bash
dotnet build --force
dotnet test tests/Sorcha.Register.Core.Tests tests/Sorcha.Register.Service.IntegrationTests \
            tests/Sorcha.Validator.Service.Tests tests/Sorcha.Peer.Service.Tests \
            tests/Sorcha.Blueprint.Service.Tests
```

Target coverage on new code ≥ 85% (constitution IV).
