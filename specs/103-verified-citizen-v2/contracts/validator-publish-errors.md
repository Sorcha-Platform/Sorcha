# Validator Publish-Time Error Contract

**Status**: New error introduced by this feature.
**Source**: Validator Service (`ValidationEngine.cs`)
**Trigger phase**: Blueprint publish

## New error: open-participant pre-binding

### Code

`VAL_BP_010` (or next available — final number assigned at implementation time. The number is reserved here as a contract; the message and trigger conditions are authoritative.)

### Severity

`Error` (blocks publication)

### Trigger

A participant referenced as the `sender` of any action with `isStartingAction: true` has a non-null, non-whitespace `walletAddress` in the published blueprint.

In code terms: for each `Action` where `IsStartingAction == true && !string.IsNullOrWhiteSpace(Sender)`, look up the participant by id and assert `string.IsNullOrWhiteSpace(participant.WalletAddress)`.

### Message template

```text
Participant '{participantId}' is the sender of starting action {actionId} ('{actionTitle}') and must have a null walletAddress so the runtime can bind the first qualifying submitter to the participant role.

Found walletAddress: '{baked-in wallet}'

To fix:
  - Remove the walletAddress field from the participant in the blueprint, OR
  - If the participant should NOT be open, remove isStartingAction from the action.

See: https://schemas.sorcha.dev/docs/open-participants
```

### Example (current vs corrected)

**Rejected** (publish fails with VAL_BP_010):
```jsonc
{
  "participants": [
    {
      "id": "citizen",
      "name": "Citizen",
      "walletAddress": "ws1qz9v25..."   // ← VAL_BP_010 fires here
    }
  ],
  "actions": [
    {
      "id": 1,
      "isStartingAction": true,
      "sender": "citizen"
    }
  ]
}
```

**Accepted** (correct shape):
```jsonc
{
  "participants": [
    {
      "id": "citizen",
      "name": "Citizen"
                              // ← walletAddress omitted; will be late-bound
    }
  ],
  "actions": [
    {
      "id": 1,
      "isStartingAction": true,
      "sender": "citizen"
    }
  ]
}
```

### Why this exists

The runtime late-binding code at `ActionExecutionService.cs:309-332` only fires when the participant's `walletAddress` is null at publish time. If the wallet is pre-baked, the strict equality check at `ActionExecutionService.cs:196-216` rejects every real public submitter with a misleading "wallet not authorized" error that doesn't point at the cause. This guardrail catches the foot-gun at publish time, where the error message can name the participant and explain the fix.

## Error format

The error follows the existing validator error shape returned by the publish endpoint. It is one of potentially many errors in the response array; partial publication is not supported.

```jsonc
{
  "errors": [
    {
      "code": "VAL_BP_010",
      "severity": "Error",
      "message": "Participant 'citizen' is the sender of starting action 1 ('Submit Application') and must have a null walletAddress ...",
      "field": "participants[0].walletAddress",
      "actionId": 1,
      "participantId": "citizen"
    }
  ]
}
```

## Related rules

This rule sits next to the existing publish-time validation rules in `ValidationEngine.cs`:

| Existing rule | Source | Relationship |
|---|---|---|
| Rule 6: At least one starting action | `Program.cs:2640-2647` | Adjacent — both validate starting actions at publish time |
| Rule (sender→participant resolution) | Existing | Adjacent — the new rule extends the existing sender validation |

## Tests required

Per the constitution's testing principle (≥85% coverage on new code), the following test cases ship with the rule:

1. **Pass**: blueprint with `isStartingAction: true`, participant has null `walletAddress` → publishes
2. **Pass**: blueprint with `isStartingAction: true`, participant has empty string `walletAddress` → publishes (whitespace counts as unset)
3. **Fail**: blueprint with `isStartingAction: true`, participant has a populated `walletAddress` → VAL_BP_010 fires with the expected message
4. **Fail**: same as (3) but the action is reachable from a different starting action via routes → VAL_BP_010 fires only for the offending action
5. **Pass**: non-starting action's sender participant has a populated `walletAddress` → publishes (the rule applies only to starting-action senders)
6. **Pass**: starting action with no `sender` field → publishes (no participant to validate)
7. **Edge case**: starting action whose `sender` references a non-existent participant id → existing sender-validation rule fires first; VAL_BP_010 does not double-report

## Performance

The check adds < 50ms to the publish path (per the plan's constraint). It iterates `Actions.Where(a => a.IsStartingAction)` once and does a `Participants` lookup per match.
