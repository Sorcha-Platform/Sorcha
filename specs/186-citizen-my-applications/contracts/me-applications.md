# API Contract: `/api/me/applications`

**Feature**: 186 | **Service**: `Sorcha.Blueprint.Service` | **Group**: `app.MapGroup("/api/me/applications")`

Personal-scope convention, matching `/api/me/inbox`, `/api/me/persona`, and `/api/me/2fa/*` in Tenant Service. This is Blueprint Service's first `/api/me` group.

**Authorization**: `.RequireAuthorization()` plain. Deliberately *not* `RequireConsumerAudience` — a citizen holds a consumer-tier token on `/wallet` and a platform-tier token on `/app`, and the same person must see the same applications from either (CLAUDE.md pattern #13: there is no "any-human" tier, so cross-tier endpoints stay plain).

**Rate limiting**: `RateLimitPolicies.Api`.

---

## `GET /api/me/applications`

Applications the caller participates in, newest first, including terminal ones.

**Query**: `page` (default 1), `pageSize` (default 20, max 100), `status` (optional `InstanceState` name filter).

**200**

```json
{
  "items": [
    {
      "instanceId": "3f2a…",
      "blueprintId": "aias-identity-assurance",
      "blueprintTitle": "Assured Identity",
      "instanceReference": "AI-CYB-14-A7K3",
      "state": "Completed",
      "outcome": "NotApproved",
      "decisionTitle": "AIAS could not assure your identity",
      "decisionReason": "The document you provided could not be read clearly.",
      "decisionSeverity": "Warning",
      "currentActionId": null,
      "currentActionTitle": null,
      "stepNumber": null,
      "totalSteps": 4,
      "needsYou": false,
      "createdAt": "2026-08-01T09:14:22Z",
      "updatedAt": "2026-08-01T09:31:07Z",
      "completedAt": "2026-08-01T09:31:07Z"
    }
  ],
  "totalCount": 1,
  "pageNumber": 1,
  "pageSize": 20
}
```

Optional fields are **omitted when absent**, never emitted as `""`. `decisionReason` in particular: an empty resolution means "this route declares no reason", and rendering that as blank text would read as a bug (FR-013).

**Caller with no resolvable wallet** → `200` with an empty page, not `403`. Matches `ListInstances`; a citizen who has not yet created a wallet has no applications, which is a truthful empty answer rather than an error.

---

## `GET /api/me/applications/{instanceId}`

**200** — the summary shape plus:

```json
{
  "steps": [
    { "actionId": 1, "title": "Submit your details",    "status": "Completed" },
    { "actionId": 2, "title": "Identity check",          "status": "Completed" },
    { "actionId": 3, "title": "Decision",                "status": "Current"   },
    { "actionId": 4, "title": "Collect your credential", "status": "Upcoming"  }
  ]
}
```

**404 / 403** — a caller who does not participate gets exactly the response a caller asking about a non-existent application gets. Reuses `InstanceParticipantGate` and the existing indistinguishable-refusal treatment from #1183, so the endpoint cannot be used to probe which application ids exist (FR-021).

---

## Deliberately unchanged

`GET /api/instances` and `GET /api/instances/{instanceId}` keep their current shape and behaviour. The PWA's `ApplicationInstance.razor` depends on the detail endpoint; the CLI binds the list one (badly — see research R7, raised separately). Neither is touched by this feature.

## Never on the wire

`decisionReasonCode` — internal classification, not citizen-facing copy (FR-014). Also absent: participant wallet addresses, accumulated data, pending action payloads, and tenant id.
