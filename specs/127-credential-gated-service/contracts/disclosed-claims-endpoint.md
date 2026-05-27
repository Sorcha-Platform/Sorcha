# Contract: `GET /api/presentations/{presentationRequestId}/disclosed-claims`

**Service**: `Sorcha.Blueprint.Service`
**Feature**: F127 (small supplement to F111's existing presentation endpoints)
**Direction**: council page (unauthenticated) → platform
**Reconciliation note**: This is the **only new HTTP endpoint** F127 adds. Everything else reuses F111's shipped surface.

## Purpose

Return the disclosed claims from a successful presentation in plaintext to the council page that initiated the presentation, so the page can autofill the second action's form. The disclosed claims are also recorded on the register (encrypted per disclosure rules) by F111's `presentation-outcome` transaction; this endpoint is a controlled plaintext view of the same data, scoped to the council page that initiated the flow.

## Authentication model

The council page is **unauthenticated** in the broader sense (it speaks to Sorcha as a third-party consumer; no user cookie, no bearer token). The token-based scheme provides the auth scope:

- F111's `InitiateAsync` returns a single-use **`ClaimsFetchToken`** alongside the `presentationRequestId`.
- The council page presents the token on this endpoint.
- The token is single-use (atomic `GetAndRemoveAsync` against `IClaimsFetchTokenStore`) and bound to a single `presentationRequestId`.
- TTL = the remaining presentation validity window.
- If the token is missing, malformed, expired, or already used → 401 + structured error.

Knowledge of `presentationRequestId` alone is NOT sufficient — the QR URI is unauthenticated and could be observed by anyone in the network path; the token is the entry credential that proves the caller is the council page that originated the flow.

## Request

```http
GET /api/presentations/8f6b94de-5e07-4b51-bba6-6e2f9b1c7a31/disclosed-claims?token=AB12cd34EFgh56IJkl78MNop=
```

| Parameter | Type | In | Required | Notes |
|---|---|---|---|---|
| `presentationRequestId` | Guid | path | yes | Must match a `presentation-outcome` (success) record on the register. |
| `token` | string | query | yes | The `ClaimsFetchToken` returned by `InitiateAsync`. Single-use; consumed on this call. |

## Response (200 OK)

```json
{
  "presentationRequestId": "8f6b94de-5e07-4b51-bba6-6e2f9b1c7a31",
  "claims": {
    "givenName": "Sarah",
    "familyName": "Example",
    "dateOfBirth": "1968-04-12",
    "homeAddress": "12 Brae Road, Strathcarron, IV54 8YQ"
  },
  "subjectDisplayName": "Sarah Example",
  "holderDid": "did:sorcha:wallet:ws11qq57wxqlhr6luzrcvt27cjh24c00kfrtdzvkfaex55rnxmukrsfmy2qg57h"
}
```

Claims filtered to the `requiredClaims` declared on the action's `credentialRequirement` — minimal disclosure preserved. The wallet's full presentation may include more; what crosses this endpoint is the strict required subset.

## Response (200 OK, status still pending)

If the council page hits this endpoint before the wallet posts the outcome (race-class case — possible when the SignalR signal fires but the outcome row hasn't fully landed):

```json
{
  "presentationRequestId": "8f6b94de-5e07-4b51-bba6-6e2f9b1c7a31",
  "status": "pending"
}
```

In this case the token is **NOT consumed** — the council page is expected to retry after the next signal / poll tick.

## Errors

| Status | Code | Cause |
|---|---|---|
| 400 | `token-missing` | `?token=` not provided. |
| 401 | `token-invalid` | Token not found in `IClaimsFetchTokenStore` (already used, expired, or never issued). |
| 401 | `token-mismatch` | Token doesn't bind to the given `presentationRequestId`. |
| 404 | `presentation-not-found` | `presentationRequestId` has no presentation-initiated record (expired or never minted). |
| 410 | `outcome-decline` | The presentation outcome was a decline — no claims to disclose. Body carries the decline reason code. |
| 410 | `outcome-abandoned` | The presentation was abandoned — no claims to disclose. |
| 429 | (standard rate-limit shape) | Rate-limit policy `RateLimitPolicies.Api` exceeded. |

## Side effects

- On the first successful return (status=success path): consumes the token via `IClaimsFetchTokenStore.GetAndRemoveAsync`. Subsequent calls with the same token return 401 `token-invalid`.
- Emits OTel span `presentation.disclosed_claims.fetched` with attributes `outcome ∈ {ok, pending, declined, abandoned, token-invalid}`.

## OpenAPI

```csharp
app.MapGet("/api/presentations/{presentationRequestId:guid}/disclosed-claims", HandleAsync)
    .AllowAnonymous()
    .WithName("GetDisclosedClaims")
    .WithSummary("Fetch the disclosed claims from a successful presentation for council-page autofill")
    .WithDescription("F127 supplement to F111's presentation lifecycle. The token returned by InitiateAsync authenticates the caller as the council page that originated the flow. Single-use; consumed on success. Claims are returned in plaintext, filtered to the requiredClaims declared on the action's credentialRequirement.")
    .RequireRateLimiting(RateLimitPolicies.Api)
    .Produces<DisclosedClaimsResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status410Gone);
```
