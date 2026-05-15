# Phase 0 Research — Sorcha Wallet enrolment inside a council application wizard

**Feature**: 126-enrol-inside-wizard
**Date**: 2026-05-15
**Status**: Complete — no NEEDS CLARIFICATION items remain.

This document captures the design decisions feeding plan.md, along with the alternatives considered. Most decisions were settled during the 2026-05-15 brainstorm (captured in `docs/superpowers/specs/2026-05-15-spec-3-enrol-inside-wizard-design.md`); a few smaller details — token wire format, return-to validation algorithm, polling cadence specifics — are resolved here.

## R-001 — Tier-detection mechanic

**Decision**: Two probes from the council page's entry-point.

- `GET /api/auth/whoami` → 200 with `{ platformUserId }` if signed in, 401 otherwise.
- `GET /api/me/devices` → active-device list (empty array = no devices).

Result drives the rendered tier:

| `/whoami` | `/me/devices.Count` | Tier | Rendered |
|---|---|---|---|
| 401 | — | 3 (cold-start) | `PreflightSignupSurface` |
| 200 | 0 | 2 (mini-gate) | `WalletPairingSurface` |
| 200 | ≥1 | 1 (fast path) | Application form |

**Rationale**: Both endpoints already exist (F114 + the existing tenant auth surface). No new server-side classification logic. Tier is recomputed on every visit, so transitions are one-way and immediate.

**Alternatives considered**:
- *A persisted "onboarding state" enum on `PlatformUser`* — rejected. The state IS the data (`HasAccount` + `HasDevice`). Persisting a redundant flag risks drift.
- *Single combined endpoint `GET /api/me/onboarding-tier`* — rejected. It would couple the council page to a server-side product concept, when the actual question is two existing facts.

## R-002 — Session token wire format

**Decision**: JWT signed by Tenant Service with the existing auth signing key. Claims:

```json
{
  "sub": "<platformUserId>",
  "scope": "enrol",
  "jti": "<uuid>",
  "iat": <epoch>,
  "exp": <iat + 600>
}
```

Single-use enforced by atomic Redis `SET NX` on the `jti` at redeem; key TTL matches the token's `exp` so the cache cleans itself.

**Rationale**:
- JWT lets the redeem endpoint validate signature + expiry + scope statelessly. Token contents are not secret (the `sub` is just a user id); the security property is "one redeem succeeds" via the JTI registry.
- 10 min TTL balances "citizen got distracted" against "stale tokens linger". Matches industry-standard short-lived bearer tokens.
- `scope: "enrol"` is a hard guard against the redeem endpoint accidentally accepting normal access tokens, and vice versa.

**Alternatives considered**:
- *Opaque random string with a Redis lookup* — rejected. Adds a stateful lookup for every redeem just to retrieve `sub`; the JWT puts that in the token.
- *Longer TTL with shorter "active" window* — rejected. Two timers add complexity; 10 min is plenty.
- *Cookie-bound token (HttpOnly cookie carries the bearer; URL has only an opaque handle)* — flagged for future hardening; defers v1.

## R-003 — Single-use enforcement mechanic

**Decision**: `IAtomicDistributedCache` from `Sorcha.AtomicCache` (the existing Feature 113 primitive). `SET NX` on `sorcha:enrol-session:{jti}` at redeem. The value carries `consumed-at` + the `displayName/email` that need to flow back to the PWA confirmation dialog.

**Rationale**:
- `IAtomicDistributedCache` is the established Sorcha primitive for "first writer wins" semantics (HAIP nonces, pre-auth codes). Reuse is correct here.
- Persisting consumer-confirmation data alongside the JTI lets the redeem call return everything in one round-trip.
- TTL on the Redis key auto-cleans expired tokens; no background sweep needed.

**Alternatives considered**:
- *Database table `EnrolSessionRedemptions`* — rejected. Adds an EF migration for state that's strictly transient.
- *In-process dictionary* — rejected. Multi-replica Tenant Service would race.

## R-004 — Return-to allowlist validation

**Decision**: Configuration-driven allowlist of trusted return-to hosts, matched as exact-host or `*.host` suffix. Validation runs on signup endpoint entry before issuing the redirect.

```json
{
  "Auth": {
    "ReturnToAllowlist": {
      "Hosts": [
        "strathcarron.gov",
        "*.strathcarron.gov",
        "localhost",
        "n1.sorcha.dev"
      ]
    }
  }
}
```

Matcher: parse the supplied `returnTo` URL with `Uri.TryCreate`; reject if scheme isn't `https` (allow `http://localhost` for dev); compare `Uri.Host` against the allowlist. Suffix matches require a leading-dot prefix on the candidate (`api.strathcarron.gov` matches `*.strathcarron.gov`).

**Rationale**:
- Configuration-driven so operators can add councils without code changes.
- Suffix matching with `*.` prefix is the standard OWASP pattern for safe subdomain trust.
- Exact-host requirement (no path prefix matching) avoids the "what counts as the same site" classic open-redirect bug class.

**Alternatives considered**:
- *Regex matching* — rejected; foot-gun for the operator config.
- *Same-origin-only* (council page must share an origin with the auth server) — rejected; the existing setup has the auth flow on the tenant-service host and council pages on their own host.

## R-005 — Pairing-completion real-time signal

**Decision**: New `TenantHub` event `DeviceEnrolled(Guid platformUserId, Guid deviceId)`. Raised by `PlatformUserDeviceService.RegisterAsync` after a successful insert. Published to the per-user group via the existing `TenantHubGroups.User(platformUserId)` builder. Subscribers (council page) join the group at component init and dispose on detach.

**Rationale**:
- `TenantHub` already exists (Feature 118); existing per-user group convention applies.
- Per-user filtering means the council page only sees events for its own session — no cross-citizen leakage.
- Single canonical raising point (`RegisterAsync` success) avoids duplicate or missing events when a device is added via different surfaces (idempotent on `(PlatformUserId, DevicePublicJwkThumbprint)` per the F114 design).

**Alternatives considered**:
- *Webhook from Wallet Service back to the council page* — rejected. The council page is a browser, not a backend; no webhook target.
- *Server-sent events* — rejected. SignalR is the established platform pattern; SSE would be a new transport for one event.
- *Tenant Service-only event (don't reuse F118 hub)* — rejected. The hub primitive is exactly right for this.

## R-006 — Polling fallback cadence

**Decision**: If `TenantHubConnection.StartAsync()` doesn't reach `Connected` within 2 seconds, the council page silently falls back to polling `GET /me/devices` every 3 seconds. Manual recovery affordance shows after 60 seconds of polling without success.

**Rationale**:
- 2 s connect timeout matches typical SignalR connection budgets on shaky networks before the user notices a stall.
- 3 s polling cadence is well within the existing `/me/devices` rate limit (`RateLimitPolicies.Api` permits hundreds/min).
- 60 s ceiling matches the spec's FR-016 — covers the "phone enrolment is taking longer than expected" without leaving the citizen forever stuck.

**Alternatives considered**:
- *Exponential backoff polling* — rejected. The work happens in a narrow window; constant cadence is simpler and the rate-limit budget tolerates it.
- *Short hub-connect timeout (e.g. 500 ms)* — rejected. Some users on cold-start mobile networks legitimately take ≥1 s to establish a fresh SignalR connection.

## R-007 — PWA-side confirmation dialog placement

**Decision**: New `EnrolmentRedeemConfirmDialog.razor` in the wallet PWA (`Sorcha.Wallet.Pwa/Components/`). Renders on `Pages/Enrol.razor` BEFORE calling the redeem endpoint. Displays the bound user's `email` + `displayName`, both of which are returned by the redeem endpoint in the same response payload (so the dialog has the info without a separate lookup). User cancel from this dialog leaves no state on the wallet device.

**Rationale**:
- Confirmation lives in the PWA, not on the council page, because the citizen holding the phone is the relevant decision-maker.
- Showing the bound user's email/name is the load-bearing mitigation per the design doc §7; cancelling MUST be a no-op (no device registered, no friction on the original user re-minting).
- Co-locating with `Pages/Enrol.razor` keeps the F114 enrolment ceremony's entry path single — the dialog gates the entry to the existing ceremony.

**Note on subtle redeem semantics**: The redeem call DOES consume the JTI (one-time-use property is preserved). If the user cancels after seeing the dialog, the original user's QR is "spent" — they need to regenerate. This is by design: a redeem call has happened; the only thing not happening is the device registration. Re-minting is one click on the council page (FR-017 / FR-018).

**Alternatives considered**:
- *Confirmation on the council page before showing the QR* — rejected. The council-page user already knows it's their session; they're not the actor at risk of mistaken pairing.
- *Two-step redeem (probe + commit)* — rejected. Adds API surface; the one-time-use property is the v1 trust model and a "probe" that doesn't consume the JTI weakens it.

## R-008 — Form-data preservation across the gate

**Decision**: Council page persists in-progress form state to browser `sessionStorage` keyed by `(applicationFormId, browserSessionId)`. Restored after the gate clears. Cleared on submission or when the citizen explicitly cancels.

**Rationale**:
- `sessionStorage` survives tab navigation within a session but doesn't outlive a closed-tab — appropriate for the "within one session" scope of FR-019.
- No server-side persistence for partial form state — keeps Spec 3 narrowly scoped (the F125 application catalogue, when it ships, may add server-side resume).
- Keyed by both form id and session id so two tabs of the same form don't trample each other.

**Alternatives considered**:
- *No preservation — citizen re-enters the form after the gate* — rejected. UX regression vs. just walking the gate before starting the form.
- *Server-side draft persistence* — deferred. Belongs with the application catalogue work in Spec 4.

## R-009 — Council page integration surface

**Decision**: Council pages consume `EnrolGateComponent` as a wrapper:

```razor
<EnrolGateComponent CouncilName="Strathcarron Council" OnReady="@HandleCitizenReady">
    <!-- the form goes here; renders only after gate clears -->
    <DrivingLicenceForm />
</EnrolGateComponent>
```

The component owns tier detection, signup redirect handling, QR/link rendering, hub subscription, polling fallback, and emits a single `OnReady` event when the citizen reaches Tier 1.

**Rationale**:
- One drop-in element per council page; consumers don't reason about tiers.
- ChildContent slot lets the form live in the consuming page (D from the brainstorm), keeping the gate component reusable across applications.

**Alternatives considered**:
- *EnrolGateComponent renders the form too* — rejected. Couples gate state with form lifecycle; F125 form rendering belongs in `SorchaFormRenderer`, not the gate.
- *Per-tier components on the council page* — rejected. Pushes tier branching to every consumer.

## R-010 — Observability — counters + spans

**Decision**: OpenTelemetry meters on a new `Sorcha.Enrolment` meter:

- `sorcha_enrol_session_minted_total` (counter, tag `purpose ∈ {tier3_first_qr, tier2_first_qr, regenerate}`)
- `sorcha_enrol_session_redeemed_total` (counter, tag `outcome ∈ {success, expired, replay, scope_mismatch, signature_fail}`)
- `sorcha_enrol_pairing_signal_latency_seconds` (histogram, tag `path ∈ {signalr, polling}`) — measured server-side as `(now - registerAsyncCompletedAt)` at the point the signal goes out.

Activity sources: `Sorcha.Enrolment.SessionMint`, `Sorcha.Enrolment.SessionRedeem`, parented to the existing HTTP request span where applicable.

**Rationale**:
- Three metrics covering the meaningful health signals (mint volume, redeem outcomes, signal latency) without metric sprawl.
- Outcome tagging on redeem surfaces the replay / scope / expiry failure modes operators care about.
- Latency histogram supports the SC-004 / FR-014 95th-percentile claim.

**Alternatives considered**:
- *Per-tier counters* — rejected. Tier breakdown emerges from `purpose` tagging on mint without doubling counter count.
- *No latency histogram* (just success counter) — rejected. SC-004 specifically measures latency.

## R-011 — Spec 1 / Spec 2 baseline preservation

**Decision**: Spec 3 adds endpoints + a hub event + a library component + a PWA dialog + a council-page composition. It does NOT modify:

- The Feature 114 device-pairing ceremony (`POST /api/v1/wallet/devices/enrol`) — the PWA still calls this after redeeming the session token.
- The Feature 124 first-credential welcome takeover — fires unchanged when the issued credential lands.
- The Feature 125 hero-row / multi-context UI on the wallet home — independent surface.
- The Feature 125 `ApplicationInstance.razor` form host — stays a stub for the future "continue on your phone" evolution.

**Rationale**:
- SC-009 demands zero regression in existing test suites.
- Each preserved surface is governed by its own spec; touching them creates a multi-spec change set with weak invariants.

**Alternatives considered**:
- *Lift `Pages/Enrol.razor` (PWA) into the gate flow as a hosted component* — rejected for v1. Keeps the route-based separation clear.

## All NEEDS CLARIFICATION resolved

Zero `NEEDS CLARIFICATION` markers in `plan.md`'s Technical Context. The brainstorm + the Spec 1/2 precedents answered all the load-bearing questions; this research closes the remaining detail-level gaps.

## Open items for Phase 1 design

Carried forward into the data-model + contracts, not litigated further here:

- Exact OpenAPI shape for the two new endpoints. Resolved in `contracts/enrol-session.openapi.yaml`.
- Exact wire shape of the redeem response (carries `displayName` + `email` for the PWA confirmation dialog). Resolved in `data-model.md`.
- Quickstart runbook for the cold-start journey on the Docker stack. Resolved in `quickstart.md`.
