# Contract: `POST /api/blueprint/presentation-requests`

**Service**: `Sorcha.Blueprint.Service`
**Feature**: F127
**Direction**: council page (Razor in `samples/strathcarron-portal/`) → platform

## Purpose

Mint a presentation request from a blueprint's starting-action credential gate. Returns the artifact the council page renders as the hybrid universal QR / tap-link / paste affordance.

## Request

```http
POST /api/blueprint/presentation-requests
Content-Type: application/json

{
  "blueprintId": "5bf2f02e-8c4a-4b14-9b3c-2d4d4f3a3e10",
  "startingActionId": "submit-blue-badge-application",
  "gateId": "assured-identity-check"
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `blueprintId` | Guid | yes | Must reference an existing published blueprint. |
| `startingActionId` | string | yes | Must be a starting action on the named blueprint. |
| `gateId` | string | yes | Must be the `id` of a `prerequisites.presentationRequests` entry on the starting action. |

## Response (200 OK)

```json
{
  "nonce": "uH8tT8qV9c9-…",
  "requestUri": "openid4vp://?client_id=did:sorcha:org:strathcarron-council&…&nonce=uH8tT8qV9c9-…",
  "qrUrl": "https://gateway.local/api/blueprint/presentation-requests/uH8tT8qV9c9-/qr",
  "tapUrl": "https://gateway.local/wallet/present?request=…",
  "expiresAt": "2026-05-15T14:32:47Z"
}
```

## Errors

| Status | Code | Cause |
|---|---|---|
| 400 | `blueprint-not-found` | `blueprintId` doesn't reference a published blueprint. |
| 400 | `action-not-found` | `startingActionId` isn't a starting action on the blueprint. |
| 400 | `gate-not-found` | `gateId` isn't declared in the action's `prerequisites.presentationRequests`. |
| 429 | (standard rate-limit shape) | Rate limit policy `RateLimitPolicies.Api` exceeded. |

## Authentication

**Public** — the council page is unauthenticated when it mints the request (the citizen may or may not be signed into Sorcha at this point). Rate-limited under `RateLimitPolicies.Api` to prevent QR-flood attacks.

## Side effects

- Creates a `PresentationRequest` entry in `IAtomicDistributedCache` keyed by `nonce`, TTL 5 minutes.
- Emits OTel span `blueprint.presentation_request.minted` with attributes `blueprintId`, `gateId`, `expiresInSeconds`.

## Observability

- Latency histogram on the endpoint (target: p95 < 200 ms — minting is in-memory + Redis SET; should be trivial).
- Counter on `blueprint.presentation_request.minted{outcome=ok|err}`.

## OpenAPI

```csharp
app.MapPost("/api/blueprint/presentation-requests", HandleMintAsync)
    .WithName("MintPresentationRequest")
    .WithSummary("Mint a presentation request from a blueprint's credential gate")
    .WithDescription("Council pages call this endpoint to obtain an OID4VP presentation request URL + nonce. The returned artifact is rendered as a hybrid universal QR / tap-link / paste affordance on the council page.")
    .RequireRateLimiting(RateLimitPolicies.Api)
    .Produces<MintPresentationRequestResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest);
```
