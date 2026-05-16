# REST Endpoint Contracts — Feature 128

All endpoints live on `Sorcha.Tenant.Service`. Existing F126 endpoints retain back-compatible behaviour; new endpoints follow the established Sorcha patterns (Minimal APIs + Scalar OpenAPI + JWT bearer + `RateLimitPolicies.PlatformAuth` unless otherwise noted).

## EXTEND: `POST /api/auth/enrol-session`

Mints an enrol-session token. Existing endpoint — adds the `mode` field.

**Request body (JSON):**
```json
{
  "returnTo": "https://example.org/strathcarron/services/driving-licence",  // optional, required iff mode=gated
  "mode": "gated"  // optional, default "gated" — values: "gated" | "standalone"
}
```

**Response 200 (JSON):**
```json
{
  "token": "...",                                  // unchanged
  "expiresAt": "2026-05-16T12:05:00Z",             // unchanged
  "mode": "gated",                                 // NEW — echoed for client convenience
  "route": "council-gate"                          // NEW — telemetry dimension, not security-bearing
}
```

**Validation:**
- `mode=gated` with no `returnTo` → 400 `mode-context-mismatch`.
- `mode=standalone` with `returnTo` → 400 `mode-context-mismatch`.
- `returnTo` URL fails allowlist (existing F126 check) → 400 `invalid-return-url`.

**Rate limit:** `RateLimitPolicies.PlatformAuth` (existing).

**Telemetry:** `sorcha_pair_mint_total{mode, route}` incremented on success.

## EXTEND: `POST /api/auth/enrol-session/redeem`

Redeems an enrol-session token. Existing endpoint — adds `mode` and `route` to the response.

**Request body:**
```json
{ "token": "..." }
```

**Response 200:**
```json
{
  "accessToken": "...",                            // unchanged — short-lived bearer for device-pairing call
  "userId": "...",                                 // unchanged
  "mode": "standalone",                            // NEW — drives copy variant on Enrol.razor
  "returnTo": null                                 // NEW — present only when mode=gated
}
```

**Errors (unchanged + extended):**
- 400 `expired-token`, `replay-token`, `malformed-token` (existing).
- 400 `mode-context-mismatch` (new) — redeem-side guard if the persisted mode/return-to combination is inconsistent.

**Telemetry:** `sorcha_pair_redeem_total{mode, route, result}` incremented per attempt. `result` values: `success`, `expired`, `replay`, `malformed`, `mode_mismatch`.

## NEW: `POST /api/auth/enrol-session/short-code`

Mints a 6-digit pairing short code wrapping an underlying `standalone` enrol-session token.

**Authentication:** required (JWT bearer for the calling user — the citizen mints short codes for themselves).

**Request body:**
```json
{ "route": "mobileweb-handoff" }   // optional, default "desktop-handoff" — telemetry dimension
```

**Response 200:**
```json
{
  "code": "847291",                                // 6-digit numeric
  "expiresAt": "2026-05-16T12:05:00Z"              // 5 minutes from mint
}
```

**Validation:**
- `route` must be one of the enumerated values (see telemetry contract). Unknown → 400 `invalid-route`.
- Collision retry up to 3 times; exhaustion → 500 `code-exhausted` (operational alert).

**Rate limit:** `RateLimitPolicies.PlatformAuth`.

**Telemetry:** `sorcha_pair_mint_total{mode="standalone", route}` incremented (same counter as token mint).

## NEW: `POST /api/auth/enrol-session/redeem-short-code`

Redeems a 6-digit short code into the underlying enrol-session token (then runs the standard redeem flow internally).

**Authentication:** required (citizen redeems their own short code from the PWA after entering it into the takeover sub-affordance).

**Request body:**
```json
{ "code": "847291" }
```

**Response 200:** same shape as `/redeem` response above, with `mode: "standalone"` always.

**Errors:**
- 400 `expired-code`, `replay-code`, `malformed-code`.
- 429 `rate-limited` — exceeded 5 attempts per code per minute.

**Rate limit:** per-code attempt counter (separate from `PlatformAuth` policy) — 5 per minute, lockout for 5 minutes.

**Telemetry:** `sorcha_pair_redeem_total{mode="standalone", route, result}`. `result` values include `expired_code`, `replay_code`, `rate_limited` in addition to the standard set.

## NEW: `GET /api/devices/has-any`

Aggregate read indicating whether the calling user has any non-revoked paired device.

**Authentication:** required (JWT bearer).

**Response 200:**
```json
{
  "hasAnyDevice": false,
  "latestEnrolledAt": null    // ISO-8601 when hasAnyDevice = true, else null
}
```

**Rate limit:** none beyond the gateway default (this is a cheap, frequently-polled read).

**Telemetry:** none on this read (high volume; no security signal).

## NEW: `POST /api/auth/pairing-resumption-email`

Dispatches the "Email me a link" resumption email for Story 2.

**Authentication:** required (citizen sends to their own bound email — server reads the email from the auth principal, never from the request body).

**Request body:** empty.

**Response 202 Accepted:** empty.

**Validation + Rate limit:**
- 3 per account per hour.
- 10 per source IP per hour.
- Exceeded → 429 `rate-limited`.

**Telemetry:** `sorcha_pair_resumption_email_total{result}` where `result ∈ {sent, rate_limited, dispatch_failed}`.

## NEW: `GET /api/auth/pairing-resumption/redeem?token=<id>`

Anonymous endpoint — re-establishes the citizen's session and redirects them into `/setup/add-device`.

**Authentication:** none (redemption itself authenticates).

**Response 302:** redirect to `/setup/add-device` on success; `/login?reason=resumption-expired` on expiry/replay.

**Telemetry:** `sorcha_pair_resumption_redeem_total{result}` where `result ∈ {success, expired, replay, malformed}`.

## Mode/context enforcement matrix (informative)

| Mint mode | ReturnTo at mint | Redeem context | Outcome |
|---|---|---|---|
| `gated` | present, allowlisted | `Enrol.razor?session=...` | success — render gated copy, redirect to `returnTo` |
| `gated` | absent | (any) | 400 `mode-context-mismatch` at mint |
| `standalone` | present | (any) | 400 `mode-context-mismatch` at mint |
| `standalone` | absent | `Enrol.razor?session=...` | success — render standalone copy, redirect to PWA Home |
| (any) | (any) | already-redeemed token | 400 `replay-token` |
| (any) | (any) | expired token | 400 `expired-token` |
