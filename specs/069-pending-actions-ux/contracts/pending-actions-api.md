# API Contract: Pending Actions Enrichment

## GET /api/actions/pending (Modified)

Returns paginated pending actions for the authenticated user's wallet, now enriched with action titles and instance references.

### Request

```
GET /api/actions/pending?page=1&pageSize=20
Authorization: Bearer {jwt}
```

### Response (200 OK)

```json
{
  "items": [
    {
      "instanceId": "9e8062af-ee19-4a3f-a378-690578ee39d9",
      "actionId": 2,
      "actionTitle": "Structural Assessment",
      "blueprintId": "construction-permit-20260326141308",
      "blueprintTitle": "Construction Permit Approval",
      "instanceReference": "CP-RIV-14W-a7k3",
      "senderAddress": "",
      "senderDisplayName": "",
      "summary": "",
      "urgency": "normal",
      "deadline": null,
      "registerId": "557d60c6df0e4412881a0fa3cd9285e2",
      "transactionId": "e8975731245c...",
      "navigationPath": null,
      "receivedAt": "2026-03-26T15:43:06Z"
    }
  ],
  "totalCount": 4,
  "page": 1,
  "pageSize": 20
}
```

### Changes from Current

| Field | Before | After |
|-------|--------|-------|
| actionTitle | `"Action 2"` (placeholder) | `"Structural Assessment"` (from blueprint) |
| instanceReference | *not present* | `"CP-RIV-14W-a7k3"` (from instance metadata) |

### Error Responses

- **401 Unauthorized**: Missing or invalid JWT
- **200 OK with empty items**: User has no pending actions (not an error)

---

## GET /api/blueprints/{blueprintId} (Existing, used for schema fetch)

Already exists. Used by the UI to fetch action schemas on-demand when TAKE ACTION is clicked.

### Request

```
GET /api/blueprints/construction-permit-20260326141308
Authorization: Bearer {jwt}
```

### Response (200 OK)

Returns full blueprint including `actions[].dataSchemas` needed for form rendering. No changes needed to this endpoint.

---

## Instance Reference in Instance Creation Response

### POST /api/instances/ (No change to request)

The response already includes `metadata`. After Action 1 completes, the `metadata.instanceReference` field will be populated.

### GET /api/instances/{instanceId} (No change)

The existing response already includes `metadata` dictionary. The `instanceReference` key will appear after first action completion:

```json
{
  "id": "9e8062af-ee19-4a3f-a378-690578ee39d9",
  "metadata": {
    "source": "walkthrough-screenshots",
    "scenario": "A",
    "instanceReference": "CP-RIV-14W-a7k3"
  }
}
```
