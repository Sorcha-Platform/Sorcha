# Phase 1 Data Model — Sorcha Wallet enrolment inside a council application wizard

**Feature**: 126-enrol-inside-wizard
**Date**: 2026-05-15

This feature adds **zero new persisted entities**. It introduces one transient cache entry (the single-use JTI registry), one new event payload, and two HTTP wire shapes. All persistent state lives in entities that already exist (PlatformUser + PlatformUserDevice from Feature 114).

## Transient: Enrolment Session JTI registry

**Storage**: `IAtomicDistributedCache` (Sorcha.AtomicCache from Feature 113). Redis-backed in production, in-memory in tests.

**Key**: `sorcha:enrol-session:{jti}` where `{jti}` is the JWT's claim.

**Value** (JSON):

```json
{
  "platformUserId": "<guid>",
  "consumedAt": "<UTC ISO 8601>",
  "displayName": "<string>",
  "email": "<string>"
}
```

**TTL**: matches the token's `exp` (10 minutes from mint). Redis cleans expired keys automatically; no background sweep.

**Lifecycle**:
- **Mint**: `EnrolSessionService.MintAsync` issues a JWT but does NOT write to the cache. The cache entry only exists once a redeem succeeds.
- **Redeem attempt**: `EnrolSessionService.RedeemAsync` validates the JWT (signature, expiry, scope), then calls `IAtomicDistributedCache.TrySetAsync(key, value, ttl, ifNotExists: true)`. First writer wins. Subsequent attempts read the existing entry's `consumedAt` to surface a replay-detected response.

**Invariants**:
- A given `jti` is consumed at most once. Replay attempts return HTTP 409 with a deterministic body shape.
- The cache entry's `displayName` and `email` are echoed from `PlatformUser` at redeem time so the PWA confirmation dialog has fresh data even if the user updated their profile between mint and redeem.

## Transient: One-time JWT (session token)

**Shape** (signed by Tenant Service signing key, same as auth JWTs):

```json
{
  "sub": "<platformUserId guid>",
  "scope": "enrol",
  "jti": "<uuid>",
  "iat": <epoch>,
  "exp": <iat + 600>
}
```

**Lifecycle**:
- Created by `EnrolSessionService.MintAsync` for the calling user's `platformUserId`. Returned to caller (the council page, which uses it to compose the QR URL).
- Sent by the PWA in the body of `POST /api/auth/enrol-session/redeem`.
- Validated, JTI consumed, response returned. JWT itself isn't persisted anywhere — the JTI registry is the only ledger.

**Invariants**:
- The token's `scope` MUST be exactly `"enrol"`. Tokens with any other scope MUST be rejected by the redeem endpoint with HTTP 400.
- `exp` MUST be ≥ 60 seconds from now AND ≤ 10 minutes from `iat`. Tokens outside that band MUST be rejected.

## Wire shape: `POST /api/auth/enrol-session` (mint)

**Request body** (signed-in caller; no extra parameters in v1):

```json
{}
```

**Response 200**:

```json
{
  "sessionToken": "<JWT string>",
  "qrUrl": "<full URL embedding the token>",
  "expiresAt": "<UTC ISO 8601>"
}
```

The `qrUrl` template is `{ConfiguredCouncilOrigin}/wallet/enrol?session={sessionToken}`. The configured council origin is per-deployment configuration; v1 ships Strathcarron's origin.

## Wire shape: `POST /api/auth/enrol-session/redeem`

**Request body**:

```json
{
  "sessionToken": "<JWT string>"
}
```

**Response 200** (successful redeem):

```json
{
  "accessToken": "<full citizen JWT>",
  "expiresIn": <seconds>,
  "displayName": "<string>",
  "email": "<string>"
}
```

**Response 400** — malformed token, wrong scope, signature invalid.
**Response 409** — token already consumed (replay).
**Response 410** — token expired.

## Wire shape: `TenantHub.DeviceEnrolled` (new hub event)

**Payload** (server-to-client, typed via `ITenantHubClient`):

```csharp
Task DeviceEnrolled(Guid platformUserId, Guid deviceId);
```

**Publishing**:
- Source: `PlatformUserDeviceService.RegisterAsync` after the new `PlatformUserDevice` row commits.
- Group: `TenantHubGroups.User(platformUserId)`.
- Idempotent: subsequent calls for the same `(platformUserId, deviceId)` republish (no-op for the citizen UX; downstream subscribers tolerate the repeat).

**Subscribing**:
- Council page: hub connection joins the `User(platformUserId)` group at component init, transitions out of the waiting state on `DeviceEnrolled`.
- PWA (optional in v1; future-proofing): can subscribe to react to "another device just got enrolled on my account" — not used by the gate but available for future surfaces (the "your account just got a new device" notification path).

## Validation rules from requirements

| Requirement | Validation |
|---|---|
| FR-009 | Session token `exp` claim ≤ `iat + 600`. Enforced on mint; revalidated on redeem. |
| FR-010 | Redeem response MUST include `displayName` + `email` for the confirmation dialog. |
| FR-011 | PWA's `EnrolmentRedeemConfirmDialog` MUST present a Cancel affordance that closes the dialog without invoking the device-pairing call. |
| FR-012 | `IAtomicDistributedCache.TrySetAsync` with `ifNotExists: true` enforces single use. |
| FR-014 | Hub event published synchronously inside the `RegisterAsync` transaction's success path. |
| FR-024 | Endpoints registered on the gateway with `RequireHttps` configured at the gateway level. |
| FR-025 | `AuthEndpoints.Login` + `AuthEndpoints.Signup` validate `?returnTo=` against `ReturnToAllowlistOptions.Hosts` before issuing the redirect; non-matches fall back to the default landing URL. |

## State transitions

### Citizen tier (derived, not stored)

```
   ┌── Tier 3 (no account) ──→ signup → Tier 2 ──→ pair → Tier 1 ──┐
   │                                                                  │
   └──── one-way; computed on every visit from /whoami + /me/devices ─┘
```

No persistent "onboarding state" enum. Tier is the answer to two probes, not a stored value.

### Session token

```
   minted ─→ in-flight (≤10 min) ─→ redeemed (JTI in cache) ─→ rejected (any further use)
                  │
                  └─→ expired ─→ rejected with 410
```

### Pairing-completion signal (council page state)

```
   waiting ─SignalR connected─→ subscribed ─DeviceEnrolled event─→ advanced
       │                            │
       │ (timeout 2 s)              │ (timeout 60 s after polling started)
       ▼                            ▼
   polling ─/me/devices ≥ 1─→ advanced
       │
       │ (60 s no result)
       ▼
   manual recovery affordance shown
```

## Entity-relationship view

```
┌─────────────────────────────────────────────────────────────────┐
│ Tenant DB (existing, unchanged)                                  │
│                                                                   │
│  PlatformUser ─────one-to-many──── PlatformUserDevice            │
│        ▲                                  ▲                       │
│        │  (FR-005: Feature 116 signup     │  (FR-013: Feature 114 │
│        │   creates this)                   │   device-pairing      │
│        │                                   │   ceremony creates    │
│        │                                   │   this)               │
└────────┼───────────────────────────────────┼─────────────────────┘
         │                                   │
         │ minted via                        │ created by
         │                                   │
┌────────┴───────────────────────────────────┴─────────────────────┐
│ Redis (IAtomicDistributedCache)                                  │
│                                                                   │
│  sorcha:enrol-session:{jti}  ─→  { platformUserId,               │
│                                    consumedAt,                    │
│                                    displayName,                    │
│                                    email }                         │
│                                                                   │
│  TTL = 10 min (matches token exp)                                 │
└───────────────────────────────────────────────────────────────────┘
```

## Cross-cutting invariants

- **Tier is derived, never persisted.** The truth lives in `PlatformUser` (account-exists) + `PlatformUserDevice` (device-exists). Any caching on the council-page side is presentation-only.
- **Session token never re-mintable post-redeem.** Citizens regenerate (mint a new one) rather than re-using.
- **Return-to validation runs server-side.** The council page MAY display the requested return URL pre-validation; the actual redirect goes through the validator on every request.
- **Confirmation dialog data flows in-band.** `displayName` + `email` come back in the redeem response so the PWA dialog doesn't need a separate user-info lookup. No extra round-trip.
- **No new EF migrations.** Spec 3 is additive at the wire surface; the EF schema is untouched.
