# Contract: Verification Transport (UI ⇄ BFF ⇄ Verifier)

This is the contract the fix relies on. It is not a new public API — it documents the **call
graph, credential per hop, and status-code → UI-state mapping** the implementation must honor.

## Trust tiers (do not change)

| Endpoint | Service | Auth policy | Caller |
|---|---|---|---|
| `POST /api/v1/verifier/requests` | HAIP | `RequireService` (SEC-013) | **service only** (Blueprint) |
| `GET  /api/v1/verifier/requests/{id}/request-object` | HAIP | `AllowAnonymous` | external wallet |
| `POST /api/v1/verifier/requests/{id}/direct-post` | HAIP | `AllowAnonymous` | external wallet |
| `GET  /api/v1/verifier/requests/{id}/result` | HAIP | `RequireService` (SEC-013) | **service only** (Blueprint) |
| `POST /api/v1/presentations/request` | Blueprint BFF (web `PresentationAdminService`) | user-authenticated | web user |
| `GET  /api/v1/presentations/{id}/result` | Blueprint BFF | user-authenticated | web user |
| `GET  /api/presentations/{id}/status` | Blueprint | `AllowAnonymous` (lifecycle-only, no claims) | wallet QR poller |

**Rule (FR-005/FR-010, Decision 2):** user-facing UI MUST call the **BFF** tier
(`/api/v1/presentations/*`), never the `RequireService` verifier endpoints directly. The service
identity (`ServiceAuthClient` client-credentials) is used **only** server-side on the
Blueprint → HAIP hop (`PresentationLifecycleService` → `IHaipServiceClient`).

### The bug being fixed

`PresentationRequestQrCard.razor` → `IHaipOfferService.GetVerificationResultAsync` →
`GET /api/v1/verifier/requests/{id}/result` (**`RequireService`**) with a **user** JWT ⇒ 401/403
⇒ swallowed to `null` ⇒ infinite silent polling. The result-poll must move to a BFF endpoint.

## Per-host credential (FR-002/FR-003/FR-004)

| Host | Mechanism (existing) | Attached to |
|---|---|---|
| Web client | `AuthenticatedHttpMessageHandler` (JWT + 401-refresh) | BFF-facing typed client (already wired on `IHaipOfferService`) |
| Wallet PWA | `BearerTokenHandler` (holder token + transparent refresh) + `ServerClockHandler` (skew) | BFF-facing typed client (PWA `Program.cs` chain) |
| Service→service | `ServiceAuthClient` (OAuth2 client-credentials) via `ServiceClientAuthHelper` | `HaipServiceClient` (Blueprint→HAIP) — **unchanged, already correct** |

## Status-code → UI-state mapping (the transport must return a discriminated outcome, not `null`)

| BFF response | UI state | Notes |
|---|---|---|
| `200` + result with terminal `State` | Verified / Denied / Expired / Cancelled | render outcome + claims |
| `200`/`202` "awaiting-presentation" / `Pending` / `Submitted` | Pending / Submitted | keep polling (bounded by `MaxPollTicks`) |
| `401` / `403` | **Error / Retry** | distinct from not-configured; after refresh fails |
| `5xx` | **Error / Retry** | transient server error |
| network / DNS failure (`HttpRequestException`) | **Error / Retry** | retryable transport error |
| `404` (request id unknown/expired at BFF) | Expired (or Error per surface) | not "not configured" |
| surface not registered for the host | **NotConfigured** (preserved) | determined at mount, not from a status code |

## Invariants (acceptance-aligned)

- I1 (FR-001/SC-001/SC-002): a configured host + reachable backend ⇒ live session; **never**
  not-configured.
- I2 (FR-006/FR-007/SC-003): any transport failure ⇒ Error+Retry; **never** blank/empty, **never**
  false not-configured.
- I3 (FR-009/SC-004): Retry after recovery ⇒ live session, without reloading the host.
- I4 (FR-008/SC-005): genuinely-unconfigured host ⇒ not-configured, unchanged.
- I5 (SEC-013/assumption #105): verifier endpoints stay `RequireService`; no service credential in
  a public client.
