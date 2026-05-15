# Contracts: `/api/blueprint/presentation-responses`

**Service**: `Sorcha.Blueprint.Service`
**Feature**: F127

Two endpoints on one resource: POST by the wallet, GET by the council page.

---

## `POST /api/blueprint/presentation-responses`

**Direction**: wallet (Sorcha.Wallet.Pwa) → platform

### Purpose

Wallet posts a signed verifiable presentation against an outstanding `PresentationRequest`. Server validates via `Sorcha.Verifier.Engine`, stashes disclosed claims keyed by nonce, fires the SignalR `PresentationReceived` event to the council page subscriber.

### Request

```http
POST /api/blueprint/presentation-responses
Content-Type: application/json
Authorization: Bearer <wallet-issued user token>

{
  "nonce": "uH8tT8qV9c9-…",
  "signedVp": "eyJhbGciOiJFZERTQSIsImtpZCI6Imhsa…"
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `nonce` | string | yes | Must match an outstanding `PresentationRequest`. |
| `signedVp` | string (compact JWS) | yes | Verifiable presentation signed by the wallet's holder key. |

### Response (200 OK)

```json
{
  "status": "accepted"
}
```

(The wallet does NOT receive the disclosed claims on POST — those are stashed server-side and fetched by the council page over GET. This keeps the wallet's posted payload minimal and the validated claims authoritative on the server.)

### Errors

| Status | Code | Cause | FR |
|---|---|---|---|
| 400 | `nonce-not-found` | `nonce` doesn't match an outstanding request (expired, consumed, or never minted). | FR-020 |
| 400 | `signature-invalid` | VP signature failed verification. | — |
| 400 | `claims-missing` | Disclosed claims don't cover the gate's `requiredClaims`. | FR-003 |
| 400 | `credential-revoked` | Status-list check (F079) returned revoked. | FR-019 |
| 400 | `issuer-not-trusted` | VP issuer DID not in the gate's `issuerAllowlist`. | — |
| 429 | (standard) | Rate-limit policy `RateLimitPolicies.Api` exceeded. | — |

### Side effects

- On success: stashes `DisclosedClaims` in `IAtomicDistributedCache` keyed by nonce (TTL extended to 10 min); removes the original `PresentationRequest` entry (single-use).
- On success: publishes SignalR event `BlueprintHub.PresentationReceived(nonce)` to the group subscribed for this nonce.
- Emits OTel span `blueprint.presentation_response.validated` with attributes `outcome`, `trustStatus`, `gateId`.
- Logs structured failure reason on rejection (without leaking the VP body).

### Authentication

**Requires user token** — the wallet's authenticated session. The wallet's holder DID must match the VP's `holder` claim.

### Observability

- `IPresentationSignal` latency histogram (from POST receipt to SignalR event dispatch — primary SC-004 verification mechanism).
- Counter on `blueprint.presentation_response.validated{outcome, trust_status}`.

---

## `GET /api/blueprint/presentation-responses/{nonce}`

**Direction**: council page (sample portal) → platform

### Purpose

Council page fetches the validated disclosed claims after it receives the `PresentationReceived` SignalR event (or via the 3 s polling fallback per FR-021).

### Request

```http
GET /api/blueprint/presentation-responses/uH8tT8qV9c9-
```

### Response (200 OK — claims available)

```json
{
  "status": "resolved",
  "claims": {
    "givenName": "Sarah",
    "familyName": "Example",
    "dateOfBirth": "1968-04-12",
    "homeAddress": "12 Brae Road, Strathcarron, IV54 8YQ"
  },
  "subjectDisplayName": "Sarah Example",
  "holderDid": "did:sorcha:wallet:ws11qq…"
}
```

### Response (200 OK — still pending)

```json
{
  "status": "pending"
}
```

(Polling fallback returns 200 with `status=pending` until the wallet's POST arrives; council page keeps polling on 3 s cadence until SignalR catches up or the 60 s manual-recovery affordance triggers.)

### Errors

| Status | Code | Cause |
|---|---|---|
| 404 | `nonce-not-found` | Nonce expired or never minted. |
| 410 | `nonce-consumed` | Claims already fetched once and the application advanced — second fetch is rejected to prevent replay. (Tactical: TBD whether 410 or stricter behaviour is right; revisit in `/speckit.tasks`.) |

### Authentication

**Public** — the council page is unauthenticated when polling. Knowledge of the nonce is the entry token; the nonce is high-entropy and short-lived. Rate-limited under `RateLimitPolicies.Api`.

### Side effects

- None on `status=pending` returns.
- On the first successful `status=resolved` return, the entry MAY be marked consumed (decision deferred to `/speckit.tasks` — depends on whether the council form needs to re-fetch on page reload).

### OpenAPI

Both endpoints documented via `.WithSummary()` + `.WithDescription()` and visible in the Scalar UI. Examples in the OpenAPI spec mirror the snippets above.
