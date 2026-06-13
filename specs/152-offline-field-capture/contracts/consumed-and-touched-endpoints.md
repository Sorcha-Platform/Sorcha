# Endpoint Contracts: PWA Offline / Field Capture

**Feature**: 152-offline-field-capture | **Date**: 2026-06-13

C is mostly device-local. It **consumes** existing endpoints unchanged and **touches one** endpoint
(US5) to honor attachments. No new endpoints.

## Consumed unchanged

| Use | Route | Notes |
|-----|-------|-------|
| Pre-cache action context (US2) | `GET /api/instances/{id}`, `GET /api/blueprints/{id}` | Same calls A's `IApplicationActionClient.LoadFormAsync` makes; cached locally for offline open |
| Pending list/count (badges) | `GET /api/actions/pending`, `/pending/count` | From A; drives which actions to pre-cache |
| In-review notice | `GET /api/v1/wallet/pending-applications` | From A |

## Touched (US5 only)

```
POST /{instanceId}/actions/{actionId}/execute    (Blueprint Service, Program.cs:2315)
```

- **Today**: accepts `ActionSubmissionRequest` (which includes `Files`) but processes only
  `PayloadData`; `Files` is ignored.
- **Change (US5)**: honor `request.Files` by reusing the **existing** logic the legacy `/api/actions`
  endpoint runs (`Program.cs:1560`): `BuildFileTransactionsAsync` → `StoreFileContentAsync` → file-
  transaction hashes referenced from the action transaction. No request/response **shape** change —
  the `Files` field already exists; it just becomes effective on this path.
- **Auth**: unchanged (`RequireAuthorization()`, consumer-tier capable).
- **Docs**: update the endpoint's OpenAPI summary/description + XML docs to note attachment support;
  add a Blueprint Service test covering submit-with-Files on the execute path.

## Conflict signals (US4) — read from existing responses

The execute response + server idempotency already surface the signals C classifies:
- success / idempotent replay → `Submitted`
- action no longer current / instance not active / already executed → `Stale(reason)`
- network / 5xx → `Retry`

No new conflict endpoint; C interprets existing outcomes.

## Large-file path (refinement, not MVP)

```
POST /api/file-chunks    (Blueprint Service — consumer-tier, XChaCha20, 10 × 4 MB)
```
Staging for media exceeding the inline `Files` payload limit; referenced from the action payload.
Used only if a capture exceeds the inline ceiling.

---

**Drift guard**: if the execute request/response or idempotency semantics change, C's conflict
classifier tests + the US5 attachment test should fail — re-align rather than mapping around it.
