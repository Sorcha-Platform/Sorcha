# Consumed Endpoint Contracts (read-only): PWA Citizen Workflow Inbox

**Feature**: 151-citizen-workflow-inbox | **Date**: 2026-06-13

A defines **no new endpoints**. It consumes the following **existing** endpoints unchanged. This
file documents the contract A relies on so any drift is caught. (No OpenAPI is added; these already
appear in the services' OpenAPI.)

## 1. List pending actions

```
GET /api/actions/pending?page={int}&pageSize={int}
Service: Blueprint Service  (ActionEndpoints.cs:27)
Auth:    Bearer (consumer-tier token works); plain RequireAuthorization() — DO NOT tighten
Tier:    cross-tier ("any-human"); citizen wallet resolved via platform_user_id
```

**Response (200)** — paginated list of `PendingActionSummary`. Fields A consumes:
`InstanceId`, `ActionId`, `ActionTitle`, `BlueprintTitle`, `InstanceReference`, `Summary`,
`Urgency` (`"normal" | "warning" | "urgent"`), `Deadline` (nullable), `ReceivedAt`,
`NavigationPath` (nullable). Other fields are ignored by the inbox.

**Semantics A depends on**: returns only actions where the citizen is the **designated sender/actor**
(their turn) and excludes actions bound to a different participant
(`EfCoreInstanceStore.IsActionForWallet`). This is the correctness guarantee behind SC-002.

**Contract test A owns**: `MyActionsClientTests` deserialises a representative JSON body (stub
`HttpMessageHandler`) into `PendingActionItem[]` with correct field + `Urgency` mapping and
unknown-urgency → `Normal`.

## 2. Pending count

```
GET /api/actions/pending/count
Service: Blueprint Service  (ActionEndpoints.cs:116)
Auth:    Bearer (consumer-tier works); plain RequireAuthorization()
```

**Response (200)**: `{ "count": int, "urgentCount": int }`. A renders `count` (FR-006);
`urgentCount` is always 0 today and is not relied upon.

## 3. Open / fill / submit (reused unchanged via IApplicationActionClient)

```
GET  /api/instances/{id}                          (load instance)
GET  /api/blueprints/{id}                          (load blueprint/action schema)
POST /api/instances/{id}/actions/{actionId}/execute (submit)
```

A does **not** call these directly — it navigates to `Pages/ApplicationInstance.razor`, which uses
`IApplicationActionClient` as today. Listed here only to record the downstream contract the inbox
hands off to. No change.

## 4. In-review notice (reused unchanged)

```
GET /api/v1/wallet/pending-applications
Service: Wallet Service  (PendingApplicationEndpoints.cs:24)
Auth:    RequireConsumerAudience  (consumer-tier)
```

**Response (200)**: the existing pending-application notice (a human-readable label). A renders it
as the "In review" banner via the existing `IPendingApplicationClient` (FR-009). No change.

---

**Drift guard**: if any of the field names/semantics above change server-side, A's
`MyActionsClientTests` (contract test) and the inbox ordering test should fail — flag and re-align
rather than silently mapping around it.
