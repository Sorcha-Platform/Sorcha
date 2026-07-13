# Data Model: AIAS decision integrity & visibility

No new persistent entity and **no EF migration**. Two new blueprint-declared shapes and one existing durable inbox record, described here for planning.

## 1. `x-claim-source` — schema property extension (US1)

A JSON-Schema property extension declaring that the field is seeded from a named JWT claim on the authenticated principal.

| Field | Type | Notes |
|-------|------|-------|
| (value) | string | The claim name to read (e.g. `"email_verified"`). |

- **Carrier**: any object property in an action's `dataSchemas[].properties`.
- **Coercion** (by the property's declared `type`): `boolean` → case-insensitive `"true"`→`true`, else `false` (fail closed, incl. absent/unparseable); other types → raw claim string only when present; no binding or no claim → not seeded.
- **Scope**: top-level properties only (nested pointers are out of scope / a documented YAGNI extension point).
- **AIAS usage**: the `emailVerified` property (`type: boolean, readOnly: true, default: true`) gains `"x-claim-source": "email_verified"`.
- **Runtime value**: the resolved boolean is written to `FormData["/emailVerified"]`, serialised into the wallet-signed payload as `"emailVerified": true|false`.

## 2. `x-decision-notice` — route annotation (US2)

An annotation on a blueprint **route** declaring that taking that route notifies a participant.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `recipientParticipantId` | string | yes | Participant to notify (AIAS: `"citizen"`, the starting participant). Resolved to a wallet via the instance participant bindings. |
| `reasonField` | string (JSON Pointer) | yes | Pointer into the just-merged action payload for the human reason (AIAS: `/verificationNotes`). |
| `title` | string | yes | Notification title (AIAS: `"AIAS could not assure your identity"`). |
| `severity` | string | no (default `"Warning"`) | Inbox severity. |

- **Carrier**: a route object in an action's `routes[]` (AIAS: the `rejected-terminal` route on action 2).
- **Trigger**: evaluated in `ActionExecutionService` when that route is the selected route for a submitted action.
- **Scope**: reject/terminal routes for this feature; approval is already notified (D5).

## 3. Applicant decision notification — existing F118 inbox record

Written via `BlueprintInboxWriter.WriteDecisionAsync` through the existing `IPlatformInboxClient` → durable `IInboxStore` (Tenant). No schema change to the inbox.

| Field | Value for a decision notice |
|-------|-----------------------------|
| `PlatformUserId` | resolved: recipient wallet → participant (`IParticipantServiceClient`) → `PlatformUserId` (`IPlatformInboxClient.ResolvePlatformUserIdAsync`). |
| `Category` | `"Workflow"` |
| `Severity` | from `x-decision-notice.severity` (default `Warning`). |
| `Title` | from `x-decision-notice.title`. |
| `Summary` | the resolved reason string (the on-brand `verificationNotes`). |
| `CorrelationKey` | `decision:{instanceId}:{actionId}` |
| `DetailHref` | `/api/instances/{instanceId}` |
| `SourceEventId` | deterministic from `(recipientWallet, instanceId, actionId, "decision-notice")` → collapses retries (FR-011). |
| `IconKey` | e.g. `workflow.rejected`. |
| `OccurredAt` | UTC now. |

Rendered by the existing F118 bell drawer with no client change; persists across sessions/devices (FR-008).

## Relationships

```
Action(2).routes[rejected-terminal].x-decision-notice
        │  recipientParticipantId ──► Instance.participantBindings[citizen].wallet
        │  reasonField ──► merged payload /verificationNotes
        ▼
BlueprintInboxWriter.WriteDecisionAsync ──► IPlatformInboxClient ──► IInboxStore (durable)
                                                                        ▼
                                                             F118 bell drawer (existing)
```
