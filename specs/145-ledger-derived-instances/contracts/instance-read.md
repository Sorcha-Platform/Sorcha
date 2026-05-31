# Contract: Instance + pending-action reads (projection-backed)

All reads are served from the materialized projection, which is identical on every node (modulo disclosure-scoped `dataView`).

## Instance state
`GET /api/instances/{instanceId}` →
```jsonc
{ "instanceId": "...", "registerId": "...", "blueprintId": "...",
  "currentActionIds": [2], "completedActionCount": 1,
  "state": "Active",                         // Active | Completed | Rejected
  "participantBindings": { "verification-analyst": "ws1...", "citizen": "ws1..." },
  "lastAppliedTxId": "..." }
```
- The same response on any node holding the register. `dataView` fields appear only for entitled callers.

## Pending actions (discovery — fixes the cross-node blocker)
`GET /api/actions/pending` (existing) is now fed by the projection:
- A participant (incl. an autonomous agent) sees their current action on **any** node that holds the register, because that node *projected* the instance to the current action — no mirror, no origination requirement (FR-014, SC-002).
- Bindings are participant-id keyed (never self-keyed), so the assignee's wallet matches.

## Advancement signal (replaces the synchronous response)
- A node emits an instance-advanced notification (existing hub/event surface) when the projection advances an instance. Callers that previously read the synchronous `nextActions` subscribe/poll this.

## Rebuild / parity (operability)
- `RebuildAsync(instanceId)` replays the instance's sealed txs to reconstruct state; used for recovery and the parity self-check (`materialized == rebuild`, FR-003 / SC-003). Exposed as an internal/operator operation, not a public mutation.
