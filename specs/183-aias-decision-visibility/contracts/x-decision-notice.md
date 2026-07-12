# Contract: `x-decision-notice` route annotation

**Owner**: `Sorcha.Blueprint.Service`. Declared on a blueprint route; evaluated in `ActionExecutionService` after route resolution. Tolerated (stripped) by schema validators via the existing `x-*` strip.

## Shape

```jsonc
// aias-assured-identity.template.json — action 2, "rejected-terminal" route:
{
  "id": "rejected-terminal",
  "nextActionIds": [],
  "isDefault": true,
  "condition": { "==": [{ "var": "decision" }, "rejected"] },
  "description": "Rejected by AIAS — workflow ends with the on-brand reason surfaced to the applicant",
  "x-decision-notice": {
    "recipientParticipantId": "citizen",
    "reasonField": "/verificationNotes",
    "title": "AIAS could not assure your identity",
    "severity": "Warning"
  }
}
```

| Field | Type | Required | Default |
|-------|------|----------|---------|
| `recipientParticipantId` | string | yes | — |
| `reasonField` | string (JSON Pointer) | yes | — |
| `title` | string | yes | — |
| `severity` | string | no | `"Warning"` |

## Writer contract (`IBlueprintInboxWriter.WriteDecisionAsync`)

```csharp
/// Drop a durable "decision" inbox entry for the recipient wallet's owning user.
/// Reuses wallet→participant→PlatformUserId resolution + deterministic idempotency.
/// Fail-safe: short-circuits on null/empty inputs or unresolved user; never throws.
Task WriteDecisionAsync(
    string recipientWalletAddress,
    string instanceId,
    string actionId,
    string title,
    string reason,
    string severity,          // default "Warning"
    CancellationToken ct = default);
```

- `Category = "Workflow"`, `Summary = reason`, `DetailHref = /api/instances/{instanceId}`,
  `SourceEventId = deterministic(recipientWallet, instanceId, actionId, "decision-notice")`.

## Hook contract (`ActionExecutionService`)

After the selected route(s) are resolved for a submitted action:

1. For each selected route carrying `x-decision-notice`:
   a. Resolve `recipientParticipantId` → wallet via the instance participant bindings (same resolution as `credentialIssuanceConfig.recipientParticipantId`).
   b. Resolve `reasonField` from the merged action payload (string; empty/absent → skip the reason but still allow a titled notice — implementation may choose to skip entirely if no reason resolves; AIAS always has `verificationNotes`).
   c. Call `WriteDecisionAsync(wallet, instanceId, actionId, title, reason, severity)`.
2. The entire block is wrapped in `try` / `LogWarning` / swallow — it MUST NOT affect sealing, routing, or the submission response.

## Acceptance

- A submitted action whose selected route carries `x-decision-notice` triggers exactly one `WriteDecisionAsync` for the resolved recipient, with `Summary` == the resolved reason.
- A route without the annotation triggers no write.
- A thrown inbox-write does not fail the submission (fault-injection).
- Replaying the same sealed decision writes at most one inbox row (idempotent `SourceEventId`).
