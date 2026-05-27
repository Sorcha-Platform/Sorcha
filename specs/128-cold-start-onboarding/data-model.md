# Phase 1 Data Model: Cold-start onboarding and device pairing UX

**Feature**: 128-cold-start-onboarding
**Date**: 2026-05-16

## Overview

This feature adds **no new persistent (PostgreSQL) tables**. It extends one existing record (the enrol-session) with a discriminator field, introduces two short-lived Redis-backed records (pairing short code, resumption token), and reads an existing PostgreSQL table (`PlatformUserDevice`) through a new aggregate endpoint.

## Existing entities (extended in-place)

### `EnrolSession` (existing F126 record)

Existing fields preserved unchanged. **New field:**

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Mode` | enum `{Gated, Standalone}` | Yes | `Gated` | Persisted at mint, enforced at redeem. `Gated` preserves all existing F126 behaviour. `Standalone` enables the four routes in this feature. |
| `Route` | string (low-cardinality) | Yes | `council-gate` | Telemetry dimension. Set at mint based on the calling context. Values: `council-gate`, `desktop-handoff`, `mobileweb-handoff`, `pwa-takeover`, `cold-landing`. Persisted alongside the session so the redeem-side telemetry can carry it. |

**Validation rules:**
- `Mode = Gated` requires `ReturnTo` to be non-null and pass the existing F126 allowlist check.
- `Mode = Standalone` requires `ReturnTo` to be null (server rejects with 400 if both are set).
- `Mode` is immutable after mint — redeem MUST NOT coerce or default.

**State transitions:** unchanged from F126 (Issued → Redeemed-or-Expired). Single-use enforced via existing `IAtomicDistributedCache` SetAsync/GetAndRemoveAsync pattern.

## New entities (Redis-backed, ephemeral)

### `PairingShortCode`

Pairs a 6-digit numeric code with an underlying `EnrolSession`. Lives in `IAtomicDistributedCache` only — never persisted to PostgreSQL.

| Field | Type | Notes |
|---|---|---|
| `Code` | string (6 digits, numeric) | Cache key suffix: `pair:shortcode:{Code}`. Random uniform pick from `100000..999999`. Collision retry up to 3 times at mint. |
| `EnrolSessionId` | Guid | Pointer to the underlying `EnrolSession` that the short code unwraps to at redeem time. |
| `MintedAt` | DateTime (UTC) | Telemetry + audit. |
| `MintedForUserId` | Guid | Audit — the citizen the short code was issued to. |
| `Route` | string | Carries the mint-time route dimension into redeem telemetry. |

**Lifetime:** 5 minutes from mint (TTL on the cache entry). Single-use enforced by `GetAndRemoveAsync` at redeem.

**Validation rules:**
- Underlying `EnrolSession.Mode` MUST be `Standalone` — short codes do not exist for gated tokens.
- Redeem rate limit: 5 attempts per code per minute (lockout via separate counter cache entry `pair:shortcode:{Code}:attempts`).

**State transitions:** Issued → (Redeemed | Expired | RateLimitedOut). Terminal.

### `PairingResumptionToken`

Email-magic-link token from Story 2's "Email me a link" affordance.

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | Cache key suffix: `pair:resumption:{Id}`. URL-safe base64 in the email link. |
| `IssuedToUserId` | Guid | Resumption is per-user; redeem MUST re-authenticate the user against this ID. |
| `IssuedAt` | DateTime (UTC) | |

**Lifetime:** 24 hours. Single-use enforced by `GetAndRemoveAsync` at redeem. Rate-limited at issue: 3 per account per hour, 10 per IP per hour.

**State transitions:** Issued → (Redeemed | Expired). Redeem re-establishes the citizen's session and routes to `/setup/add-device`.

## Existing entities (read-only access via new aggregate endpoint)

### `PlatformUserDevice` (existing F114 table)

No schema change. New read path: aggregate `has-any` query, returning `{ hasAnyDevice: bool, latestEnrolledAt: DateTime? }` for the calling user.

Query: `SELECT COUNT(*) > 0, MAX(EnrolledAt) FROM PlatformUserDevice WHERE UserId = @userId AND Revoked = false`.

**Caching:** Client-side per-session cache in `HasPairedDeviceProbe`, invalidated by `TenantHub.DeviceEnrolled` event (existing F126 hub publish) and by a local `PairingCompleted` event (covers same-device pair-success where the hub event might race the takeover dismissal). Server side: uncached — one cheap DB read per signin and per probe call is acceptable.

## Relationships

```
EnrolSession  ──1:0..1──  PairingShortCode   (short code is a transport wrapper)
EnrolSession  ──0:N──     PlatformUserDevice (redeem creates a device row via existing F114 ceremony)
PlatformUser  ──1:0..N──  PairingResumptionToken
PlatformUser  ──1:0..N──  PlatformUserDevice
```

The short code does not replace the enrol-session — it wraps one. Redeem-short-code internally unwraps to the underlying session ID and runs the standard enrol-session redeem flow.

## Concurrency and idempotency

- **Short-code redeem race:** `GetAndRemoveAsync` is atomic on `IAtomicDistributedCache`; concurrent redeems for the same code result in one success and one "already used" failure.
- **Enrol-session redeem race:** unchanged from F126 (same pattern).
- **Has-any probe staleness:** the client cache invalidates on the hub event AND on local pair-success; a transient stale window (≤ event-delivery latency) is acceptable — the probe is advisory for UX, not authoritative for auth.
- **Pairing-completed hub event:** `TenantHub.DeviceEnrolled` (existing F126 event) is idempotent — listeners must tolerate duplicate publishes (existing constraint).

## No schema migrations required

The `Mode` and `Route` additions to `EnrolSession` are on a record that already lives in the existing F126 `EnrolSession` storage (currently `IAtomicDistributedCache`-backed per the F126 design — there is no SQL table for this entity). Storing the new fields requires only an update to the DTO + value-type serialization. No EF Core migration is generated.

## Out-of-scope data concerns

- Device revocation lifecycle — unchanged from F114.
- Cross-context persona — pairing is per-account; the persona table is untouched.
- Audit logging — covered by the existing F126 audit trail extended with the new `Mode` and `Route` dimensions (see telemetry contract in `contracts/`).
