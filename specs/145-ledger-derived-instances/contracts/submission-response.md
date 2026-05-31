# Contract: Unified submission response (single async path)

One path for `POST /api/instances/{id}/actions/{actionId}/execute` regardless of register ownership. The submitter never mutates instance state; state advances via the projection on seal.

## Request
Unchanged (action payload + sender). Starting-action submit additionally returns the canonical `instanceId`.

## Response

Always one of two shapes from the **same** path (the difference is timing, not branch):

```jsonc
// 202 Accepted — the default; projection will advance on seal
{ "txId": "...", "instanceId": "...", "accepted": true, "pending": "awaiting-seal" }

// 200 OK — bounded-wait convenience: the projection advanced within the wait window
{ "txId": "...", "instanceId": "...", "accepted": true,
  "currentActionIds": [2], "isComplete": false }
```

- The endpoint MAY wait up to `BoundedWaitSeconds` (default ~2–3s, configurable) for the projection to advance (signalled via `instance-advanced:{instanceId}`); on timeout it returns `202`.
- **No field distinguishes "owner" from "subscriber"** and no behaviour selects on topology (SC-006).
- `nextActions` and inline `issuedCredential` are **removed** from the submit response. Callers resolve outcomes by observing instance advancement (instance read / hub event) and credential availability (credential events) — see `instance-read.md`.

## Sealer selection (fan-out)
`TransactionDistributionService` selects the sealing target by `IRegisterLocalRelationshipService` roster membership (who is on the validator roster), not by seed/topology. F143 relay transport unchanged.

## Removed
- The `!LocallyOwned && AcceptedCount>0` branch and the synchronous confirmation-wait path.
- Inline credential issuance on the submit path (moves to reactions).
