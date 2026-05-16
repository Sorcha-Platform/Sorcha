# Cold-start onboarding + device pairing UX — design

**Date:** 2026-05-16
**Status:** Draft, brainstorm output
**Branch:** `cold-start-brainstorm-2026-05-16`
**Successor to:** F126 (council-page cold-start gate, shipped at `spec-126-complete`)
**Sits inside:** Strathcarron citizen arc (see `2026-05-13-strathcarron-citizen-arc.md`)

## 1. Problem

Spec 1–4 of the Strathcarron arc gave Sarah a working credential journey when she arrives through a council page. The council-page cold-start (F126) handles "citizen lands on a council form, needs an account + wallet to proceed" cleanly.

Three citizen-onboarding paths *outside* the council-page entry remain underdesigned:

1. **Desktop → phone.** Sarah signs up on a laptop at sorcha.dev or on a desktop council page; she now needs the wallet on her phone. Today the only path is install the PWA, sign in, navigate to Settings → "Enrol this device." That is hidden — citizens have to know to look.
2. **Mobile web → same-phone PWA.** Sarah signs up in mobile Safari; she wants the wallet installed on the same phone. Today she does signup, install, sign-in, Settings → Enrol as three discrete journeys.
3. **In-PWA, signed in but not paired.** A signed-in citizen opens the PWA with no paired device for this hardware. The PWA today shows a normal Home with placeholders and the pair affordance is again hidden behind Settings.

A fourth case — **citizen discovers the Sorcha PWA cold from an app store** — is not a real acquisition channel (Sorcha is a B2B2C platform; citizens reach it via a council/service), but app-store presence still needs a graceful landing.

The operator's flagged irritant: "Settings → Enrol this device" being the *primary* discoverability path for routes 2, 3, and 4 is wrong. Pairing is part of first-time setup, not a power-user option.

This design covers the structural pass on those four routes. It does not redesign the account model (F116), the device-pairing cryptography (F114), or the council-page gate (F126) — those are reused unchanged.

## 2. Decisions

| Route | Decision |
|---|---|
| 1 — PWA cold from app store | Static landing at `sorcha.dev/get` (final path TBD). "Sorcha is for services that use the Sorcha platform — find your council, sign up there, come back." Two affordances: service listing + "I already have an account → sign in." Deprioritized; minimal investment. |
| 2 — desktop → phone | Post-signup gate (dismissable full-page surface, QR-based) + persistent "Add my phone" menu item under existing F114 My Devices page. Soft-nag banner sits across Sorcha Web after a Skip. |
| 3 — mobile-web → same-phone PWA | Same gate as route 2, but detects PWA-installable mobile browser and renders "Install Sorcha Wallet" instead of a QR. Seamless `start_url`-baked token where the platform honors it; 6-digit short-code fallback for iOS quirks. |
| 4 — in-PWA, signed in but not paired | Full-page takeover on PWA launch until paired. No nav, no skip — pairing IS the screen. Short-code entry sub-affordance for route-3 fallback. |
| Primitive | One `enrol-session` token (existing F126 endpoint), grows `mode: "gated" \| "standalone"` field. Single redeem URL (`Enrol.razor?session=<token>`) renders two copy variants based on `mode` echoed in the redeem response. No separate `Pair.razor` — copy variation is not a routing concern. |

## 3. Architecture

### 3.1 Token primitive

`POST /api/auth/enrol-session` (existing F126 endpoint):
- Accepts optional `mode: "gated" | "standalone"` body field. Default `"gated"` preserves F126 callers.
- Records `mode` on the session for audit and downstream enforcement.

`POST /api/auth/enrol-session/redeem` (existing F126 endpoint):
- Response payload includes `mode` echo so the redeem page can pick copy/destination.
- Server-side reject if mode/returnTo combination is incoherent (gated token without returnTo → error log + 400; standalone token with returnTo → strip returnTo, log warning).

`Enrol.razor?session=<token>` (existing F126 page):
- Reads `mode` from redeem response.
- `gated` → "We'll bring you back to your application" copy + redirect to `returnTo` after pair.
- `standalone` → "You're set up" confirmation + redirect to PWA Home after pair.

The cryptographic device-pairing ceremony (F114 holder-key + per-device delegation) is unchanged. This design only adds new *entry points* into the same ceremony.

### 3.2 Short-code transport (new — for route 3 fallback and route 4 secondary affordance)

`POST /api/auth/enrol-session/short-code`:
- Mints a `standalone` enrol-session token plus a paired 6-digit numeric short code with ~5 min TTL.
- Stored in `IAtomicDistributedCache` (Sorcha.AtomicCache), keyed by short code, value = underlying enrol-session token reference. NonceStore pattern: SetAsync-at-create + GetAndRemoveAsync-at-consume.
- Rate-limited via `RateLimitPolicies.PlatformAuth`.

`POST /api/auth/enrol-session/redeem-short-code`:
- PWA-side endpoint. Exchanges short code for the underlying token, then runs the normal redeem path internally.
- Single-use enforced by GetAndRemoveAsync.

### 3.3 Tier-aware post-signup gate

A new full-page surface in Sorcha Web — strawman path `/setup/add-device`. Reached automatically after first successful signup (Login.cshtml.cs + Signup.cshtml.cs route there on success when the user has zero paired devices) and reachable manually from the "Add my phone" affordance under My Devices.

The gate detects which route it's serving via UA + install-prompt availability:

- **Desktop or mobile browser without PWA-install affordance →** route-2 variant: large QR encoding the PWA's `/enrol?session=<token>` URL + "Don't have your phone? Email me a link" + "Skip for now."
- **Mobile browser with PWA-install affordance →** route-3 variant: "Install Sorcha Wallet" button that triggers Add-to-Home-Screen with the token baked into `start_url`. Below: "Already installed? Open the app and enter this code: 123456" (the short-code fallback, always shown so the citizen has an out if seamless fails).

Skip dismisses the gate, drops the citizen on Sorcha Web with a persistent top-of-page banner ("You haven't paired a phone — credentials can't be received. [Pair my phone]") that re-opens the gate.

### 3.4 Route-4 takeover

A new `PairingTakeover` component in `Sorcha.UI.Components.User.Components.Pairing` (audience-partitioned per the F123 convention; namespace stays `Sorcha.UI.Core.Components.Pairing` via the Components.User RootNamespace rebinding).

Mounted from the PWA's `MainLayout` outside `MudContainer`, conditional on `IUserContext.IsAuthenticated && !HasPairedDevice` (probed at launch and refreshed on `TenantHub.DeviceEnrolled`).

Surface:
- Headline: "Set up this device"
- Primary button: "Set up" → triggers F114 device-pairing ceremony in-place (citizen is already authenticated in the PWA, so no token redeem is needed — the pairing call uses the existing session).
- Expandable sub-affordance: "Already started on another device? Enter pairing code" → short-code input field → `POST /api/auth/enrol-session/redeem-short-code`.
- No nav, no skip. Pairing is the screen until done.

Settings → "Enrol this device" stays as a power-user / re-pair entry point but is no longer the primary discovery path.

### 3.5 Shared `HasPairedDeviceProbe`

Small client-side service in `Sorcha.UI.Components.User`, consumed by both `Sorcha.UI.Web.Client` (for the Sorcha Web nag banner trigger) and `Sorcha.Wallet.Pwa` (for the takeover trigger).

- `GET /api/devices/has-any` (new endpoint) returns `{ hasAnyDevice: bool, latestEnrolledAt: timestamp? }` for the signed-in user.
- Cached for the session; invalidated on `TenantHub.DeviceEnrolled` event so the takeover dismisses instantly on pair-success.
- Local pair-success also fires a local event the probe listens to (covers PWA self-pair from the takeover button, where the hub event might race the navigation).

## 4. Surfaces — build vs reuse

**Reuse without modification:**
- F114 device-pairing ceremony, `IPlatformUserDeviceService`, `TenantHub.DeviceEnrolled` publish.
- F116 signup PageModels + return-url allowlist behavior. Gate sits *after* signup completes; signup itself is untouched.
- F126 `EnrolGateComponent`, `IEnrolPairingSignal`, tier-probe wiring. Council-page path keeps minting `mode=gated` tokens.
- F112 transactional email facade for the "email me a link" resumption flow.

**Extend (small additions to existing surfaces):**
1. `POST /api/auth/enrol-session` — accept `mode` field (default `gated`).
2. `POST /api/auth/enrol-session/redeem` — include `mode` echo in response payload + enforce mode/returnTo coherence.
3. `Enrol.razor` — render two copy variants based on `mode`.
4. Sorcha Web `Login.cshtml.cs` + `Signup.cshtml.cs` — on first-signup success with zero paired devices, route to `/setup/add-device`.
5. My Devices page (F114) — add "Add my phone" affordance that opens the gate page.

**New (the minimum that didn't exist before):**
1. `POST /api/auth/enrol-session/short-code` + `POST /api/auth/enrol-session/redeem-short-code` endpoints.
2. `PairingTakeover` component (route 4).
3. `/setup/add-device` post-signup gate page (routes 2 + 3, tier-detected at render time).
4. `HasPairedDeviceProbe` shared service + `GET /api/devices/has-any` endpoint.
5. Soft-nag banner component in Sorcha Web (triggered by the probe).
6. Marketing landing at `sorcha.dev/get` (route 1) — static Razor page.
7. Resumption-email template under `Sorcha.Tenant.Service/Emails/Templates/` for the "email me a link" affordance.

**Platform-vs-consumer boundary note:** The marketing landing's *service listing* (when populated with named services like Strathcarron) is consumer-flavoured and should sit in `samples/` per the 2026-05-15 boundary rule. The generic "what is Sorcha" copy + sign-in entry stays in `src/Apps/Sorcha.UI.Web`. Defer the split until Spec 5 or beyond forces the question.

## 5. Telemetry

All four routes flow through the same mint + redeem endpoints. Existing F126 structured logging + audit covers them with one addition: include `mode` (and a derived `route` dimension when distinguishable from server context) on the existing log fields so route mix is graphable in production.

Pair-success and skip events on the post-signup gate emit OTel counters so the >30% skip-rate risk threshold (Section 6) can be monitored.

## 6. Risks and open questions

**Risks:**
- **Same-device install + QR is impossible.** Route 3's PWA-installable detection must be reliable. Wrong-route classification on iOS = citizen trying to scan their own screen. Mitigation: always show the short-code fallback inline on the route-3 variant.
- **iOS `start_url` query persistence behavior.** Route 3's seamless path depends on iOS honoring `?session=` on first home-screen launch. Empirically verify on iOS 17 + 18 during implementation; short-code fallback is the regression net.
- **Soft-nag banner-blindness.** Citizens learn to ignore banners. If telemetry shows >30% Skip-then-never-pair rate, revisit the dismissable decision.
- **`mode=gated` token misuse.** A gated token redeemed without returnTo should hard-fail server-side; don't silently downgrade to standalone.

**Open questions (settle in plan/implementation):**
- Short-code shape (6-digit numeric vs 4+2 alphanumeric vs 8-digit) and exact TTL.
- `HasPairedDeviceProbe` cache invalidation event ordering (hub event vs local event race conditions on self-pair).
- Final URL for the post-signup gate page (`/setup/add-device` strawman).
- Resumption-email template copy.
- Whether a CI grep gate should warn against new code paths that link to Settings → Enrol as the *only* pairing affordance.

## 7. Out of scope

- Sorcha account model (email/password anchor invariant — F116 stands).
- F114 cryptographic ceremony (holder-key + per-device delegation — unchanged).
- F126 council-page cold-start (`mode=gated` token contract is back-compat preserved).
- Multi-device threat model (existing F114 limits reused unchanged).
- Cross-context persona (F125) — pairing is per-account, not per-context.
- "Lost my phone" recovery — existing F114 backlog, not addressed here.
- Native iOS/Android app shells — PWA-only.
- Push notifications.

## 8. Implementation note

This is a brainstorm output. Implementation lands under its own spec/plan/tasks cycle — likely a new feature number under the Strathcarron arc, sequenced after Spec 4 (F127) and ahead of or alongside Spec 5 (MyStrathcarron portal). The branch `cold-start-brainstorm-2026-05-16` commits this document only; no code change.
