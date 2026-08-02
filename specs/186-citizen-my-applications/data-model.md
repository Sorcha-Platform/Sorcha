# Data Model: Citizen "My Applications" View

**Feature**: 186 | **Date**: 2026-08-02

## 1. `Instance` — two new fields

`src/Services/Sorcha.Blueprint.Service/Models/Instance.cs`

| Field | Type | Nullable | Source |
|---|---|---|---|
| `DecisionRouteId` | `string?` | yes | `RoutingDecision.RouteId` on the folded transaction's clear metadata |
| `DecisionReasonCode` | `string?` | yes | `RoutingDecision.ReasonCode`, same source |

**Both are projected, never authored.** They are written only by `InstanceProjection.ApplyInPlace` and only from signed clear ledger metadata, which is what keeps the fold deterministic across nodes and identical under rebuild.

**Lifecycle**: set on every fold that carries a routing decision; **cleared** on a fold that carries none. Clearing matters — without it an application that is refused on one branch and then advances on another would keep showing the old reason.

**Both null** for: transactions sealed before Feature 184, presentation-outcome decisions (which carry no route id by design), and any route that declares no `x-decision-notice` and no reason-code field.

### Persistence

`InstanceEntity` gains two `text` columns of the same names. Registered in `BlueprintDbContext`, added to the amended `InitialCreate` migration and the model snapshot, and — critically — added to the hand-written model→entity copy list in `EfCoreInstanceStore.UpdateAsync`. A field absent from that list is written in memory, reported saved, and silently lost.

## 2. `ProjectedTransaction` — two new members

`src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjection.cs`

```csharp
public sealed record ProjectedTransaction(
    string TxId,
    string? PreviousTransactionId,
    int CompletedActionId,
    IReadOnlyList<int> NextActionIds,
    IReadOnlyDictionary<string, string> ParticipantBindings,
    bool IsRejection = false,
    string? RouteId = null,
    string? ReasonCode = null);
```

Appended with defaults so every existing construction site keeps compiling. `IsRejection` is left exactly as found — see research R2 for why it is dead and why this feature does not revive it.

## 3. `MyApplicationSummary` — the list projection

`src/Services/Sorcha.Blueprint.Service/Models/MyApplicationDto.cs`. Serialised camelCase.

| Field | Type | Notes |
|---|---|---|
| `instanceId` | `string` | |
| `blueprintId` | `string` | |
| `blueprintTitle` | `string` | `Metadata["BlueprintTitle"]` → blueprint lookup → the id |
| `instanceReference` | `string?` | `Metadata["instanceReference"]`; omitted when the first action has not sealed |
| `state` | `string` | `InstanceState` **name**, never its integer |
| `outcome` | `string` | citizen-facing outcome, derived (§5) |
| `decisionTitle` | `string?` | the taken route's notice title |
| `decisionReason` | `string?` | `DecisionNotice.ResolveMessage(code)`; **omitted when empty** |
| `decisionSeverity` | `string?` | notice severity, defaulting to `Warning` as the dispatcher does |
| `currentActionId` | `int?` | first current action; null when terminal |
| `currentActionTitle` | `string?` | |
| `stepNumber` | `int?` | 1-based position of the current action |
| `totalSteps` | `int?` | count of actions on the blueprint |
| `needsYou` | `bool` | §6 |
| `createdAt` | `DateTimeOffset` | |
| `updatedAt` | `DateTimeOffset` | |
| `completedAt` | `DateTimeOffset?` | |

`MyApplicationDetail` extends this with `steps` — an ordered list of `{ actionId, title, status }` where status is `Completed` / `Current` / `Upcoming`.

**Never on the wire**: `DecisionReasonCode` (FR-014), participant wallet addresses, accumulated data, pending payloads, tenant id.

## 4. Envelope

```json
{ "items": [ … ], "totalCount": 0, "pageNumber": 1, "pageSize": 20 }
```

Matches the client's existing `PaginatedList<T>` field-for-field, and matches what `/api/instances` already returns — so the CLI's separate envelope defect (research R7) is not propagated into a second endpoint.

## 5. Outcome derivation

Ordered; first match wins.

| # | Condition | `outcome` | Reason fields |
|---|---|---|---|
| 1 | `DecisionRouteId` is null | `state` name | none |
| 2 | Blueprint or route unresolvable on this node | `state` name | none |
| 3 | Route declares no `x-decision-notice` | `state` name | none |
| 4 | Notice severity is `Warning` or `Error` | `NotApproved` | title, reason, severity |
| 5 | Otherwise | `state` name | title, reason, severity |

Row 4 is the whole point: a refusal that ends the application leaves `InstanceState.Completed`, so without this a refused citizen is told their application "completed" (spec FR-027).

Row 3 is FR-013 — no notice means no reason, and never invented wording. Note `ResolveMessage` returns `FallbackMessage ?? ""`, so an empty result must be treated as "no reason" and the field omitted rather than rendered blank.

## 6. `needsYou`

True when the instance is non-terminal **and** any current action's sender participant is bound, through `Instance.ParticipantWallets`, to one of the caller's resolved wallets.

False whenever the instance is terminal, the blueprint is unresolvable, or the binding is absent — failing closed, so the page never offers an action that cannot be taken. This is what dissolves #1268.

## 7. Client model

`WorkflowInstanceViewModel` is rewritten to mirror `MyApplicationSummary` exactly, dropping `Status` (the field that silently defaulted to `"active"` because the server sends `state` as an integer) and `ParticipantCount` (never sent, and not needed by any page). `PaginatedList<T>` is unchanged.
