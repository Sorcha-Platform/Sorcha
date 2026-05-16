# Telemetry Contracts — Feature 128

All counters live on the `sorcha` OTel meter (existing). Dimensions are low-cardinality strings. No PII. No high-cardinality identifiers.

## Dimensions used across this feature

| Dimension | Type | Values |
|---|---|---|
| `mode` | enum | `gated`, `standalone` |
| `route` | enum | `council-gate`, `desktop-handoff`, `mobileweb-handoff`, `pwa-takeover`, `cold-landing` |
| `result` | enum | counter-specific, see each counter |
| `platform` | enum | `ios-safari`, `android-chrome`, `desktop`, `other` — only on counters where platform-fork analysis is needed (SC-006) |

## Counters

### `sorcha_pair_mint_total{mode, route}`

Incremented once per successful enrol-session mint OR pairing-short-code mint.

Used for: SC-005 per-route mix dashboard; baseline against which redeem/skip funnels are normalised.

### `sorcha_pair_redeem_total{mode, route, result}`

Incremented once per redeem attempt (token redeem and short-code redeem combined). `result` values:
- `success`
- `expired` (token TTL exceeded)
- `expired_code` (short-code TTL exceeded)
- `replay` (token already consumed)
- `replay_code` (short code already consumed)
- `malformed`
- `mode_mismatch` (gated/standalone enforcement triggered — SC-007 verifier)
- `rate_limited` (short-code per-code attempt-limit triggered)
- `ceremony_failed` (downstream device-pairing ceremony failed — surfaced for recoverable-error handling)

### `sorcha_pair_handoff_skip_total{route}`

Incremented when the citizen explicitly clicks "Skip for now" on the desktop handoff (Story 2) or the install handoff (Story 3). Drives SC-003 measurement.

### `sorcha_pair_shortcode_fallback_total{route, platform}`

Incremented when the citizen uses the short-code path on the PWA takeover sub-affordance after the seamless install path. Together with `sorcha_pair_redeem_total{result=success, route="mobileweb-handoff"}` this gives the SC-006 ratio (seamless-success vs short-code-fallback) per platform.

### `sorcha_pair_takeover_render_total{result}`

Incremented when the PWA renders the takeover. `result` values: `shown` (citizen had zero devices) or `skipped` (had ≥1 device, takeover did not render). Used to verify FR-010 / SC-004 in production.

### `sorcha_pair_takeover_dismissed_total{cause}`

Incremented when the takeover dismisses. `cause` values:
- `local-pair-success` (citizen completed pairing on this device)
- `remote-pair-success` (hub event for a pair on another device)
- `short-code-redeem-success`

### `sorcha_pair_resumption_email_total{result}`

`result ∈ {sent, rate_limited, dispatch_failed}`.

### `sorcha_pair_resumption_redeem_total{result}`

`result ∈ {success, expired, replay, malformed}`.

### `sorcha_pair_nag_banner_total{event}`

`event ∈ {shown, dismissed, clicked-through}`. Tracks the Story 2 fallback banner engagement.

## Structured-log dimensions (correlated to counters)

All structured-log entries for pairing flows MUST carry `mode`, `route`, and (where applicable) `result` and `code_id_hash` (SHA-256 of short code, never the code itself — for incident correlation without exposing the code in logs).

## SLOs (informative, derived from spec Success Criteria)

| SLO | Source | Target |
|---|---|---|
| Pair-on-PWA-cold-start latency (p95) | takeover render → dismiss `local-pair-success` | < 30s (SC-001) |
| Desktop-handoff completion-in-session rate | mints with redeem-success / mints | ≥ 80% (SC-003) |
| Mobile-web seamless-install success rate (Android-Chrome) | redeem-success with `route=mobileweb-handoff` not preceded by `shortcode_fallback` | ≥ 50% (SC-006) |
| Mode-mismatch successful-coercion count | `redeem_total{result=success}` where mint/redeem mode differ | 0 (SC-007 — there must never be a non-zero value here) |
| F126 council-gate success-rate regression | `redeem_total{result=success, route=council-gate}` / `mint_total{route=council-gate}` | ± 2pp of pre-feature baseline (SC-008) |

## No new traces or spans

Existing F126 spans (`enrol-session.mint`, `enrol-session.redeem`) are reused with the additional `mode` and `route` span attributes. No new span names are introduced. The short-code endpoints reuse the same span names (one span per mint, one per redeem) with attribute `pair.transport = "short-code"` to distinguish from token-direct paths in trace analysis.
