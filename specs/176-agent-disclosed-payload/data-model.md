# Phase 1 Data Model: Autonomous agent decides on disclosed application data

**Feature**: 176-agent-disclosed-payload | **Date**: 2026-07-07

No new persisted entities. The feature moves existing data (the disclosed prior-action payload) across an
existing boundary. Entities below are transport/behavioural shapes.

## Entities

### DisclosedActionData (response of the disclosed-data endpoint)

The subset of an instance's accumulated action data that is disclosed to the **calling participant**, keyed by
the action it originated from.

| Field | Type | Notes |
|---|---|---|
| `instanceId` | string | The workflow instance. |
| `actionId` | int | The action being decided (the one whose prior data is requested). |
| `registerId` | string | Register the instance lives on. |
| `disclosedFields` | object (JSON) | The merged, disclosed prior-action payload as a JSON object — the fields the calling participant is entitled to see (e.g. `/name`, `/address`, `/email`, `/portrait`). This is what the agent feeds to its checks as `PreviousPayload`. |
| `disclosedByAction` | map<int, object> *(optional)* | Same data partitioned by originating action id, when the caller needs provenance. |
| `recipientResolved` | bool | True when the caller's wallet was resolved and disclosure applied; false → the caller is not a disclosure recipient (drives fail-closed). |

**Shape alignment**: MUST match what `IBlueprintServiceClient.GetDisclosedDataAsync()` already deserialises
(`BlueprintServiceClient.cs:362-368`). If the existing client type differs, the client contract is the
authority — the endpoint is written to it (the client + MCP `DisclosedDataTool` are existing consumers).

**Validation / disclosure rules**:
- Only fields disclosed to the caller's participant appear (`ApplyDisclosures` engine result). No field the
  applicant did not disclose to that participant is ever present (FR-006/FR-010).
- Identical view whether the register stores payloads encrypted or in dev-mode plaintext (the endpoint returns
  the disclosed/decoded-for-recipient view).

### CheckFacts (agent, existing — now populated from real data)

Produced by `ExternalCheckRunner.RunAsync(payload)` over the disclosed payload; merged under the `checks` key
for the rules engine. Unchanged in shape; changed in that `payload` is now the real disclosed data, not `{}`.

| Field | Type | Notes |
|---|---|---|
| `checks.<name>` | bool | One per configured check (e.g. `postcodeExists`, `emailVerified`, `photoPresent`, `profane`). |
| `checks.<name>Detail` | string? | Optional detail (e.g. the queried postcode). |

### AgentDecision (existing — now correct)

| Field | Type | Notes |
|---|---|---|
| `decision` | enum | `approve` \| `reject` \| `hold`. |
| `payload` | object? | The action payload the agent submits (carries the domain `decision`/`verificationNotes`). |
| `reasoning` | string? | Human-readable reason (e.g. the on-brand rejection reason). |

**New transition**: `hold` is now also produced when the disclosed payload required by the rules is
unavailable/empty (FR-005), in addition to the existing #1077 "checks unavailable" hold.

## State / decision transitions (agent, per pending action)

```
discover pending action
   │
   ▼
fetch DisclosedActionData for (instanceId, actionId)   ──fetch fails / recipientResolved=false──▶  HOLD  (fail-closed)
   │ success, disclosedFields present
   ▼
PreviousPayload := disclosedFields
   │
   ▼
ExternalCheckRunner.RunAsync(PreviousPayload) → CheckFacts
   │                                   (rules require checks but facts empty ──▶ HOLD, #1077 existing)
   ▼
RulesDecisionEngine evaluates rules over CheckFacts
   │
   ├─ a reject rule matches ─────▶ REJECT (submit action 2 with rejected payload; no credential)
   └─ catch-all matches ─────────▶ APPROVE (submit action 2 with approved payload; credential issued)
```

## Identifiers the agent already has (from `PendingActionSummary`)

`instanceId`, `actionId`, `registerId`, `blueprintId`, `transactionId` — sufficient to key the disclosed-data
fetch (the endpoint reconstructs prior-action data from the instance's sealed transactions and applies
disclosure for the caller's wallet).

## Non-goals (data)

- No new stored entity, table, or migration.
- No change to disclosure rules or which participant is entitled to which field.
- No change to the `PrepopulatedPayload` (Feature-104) semantics.
