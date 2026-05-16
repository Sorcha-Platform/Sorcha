# Phase 0 Research: Cold-start onboarding and device pairing UX

**Feature**: 128-cold-start-onboarding
**Date**: 2026-05-16
**Status**: Resolved — no open NEEDS CLARIFICATION remain

## R1 — iOS PWA `start_url` query persistence on first home-screen launch

**Question:** Does iOS Safari honor query parameters appended to `start_url` (e.g., `?session=<token>`) when the user launches the just-installed PWA from the home-screen icon? This determines whether Story 3's seamless path can work on iOS at all, or whether we ship as short-code-only on iOS from day one.

**Decision:** Treat iOS as **seamless-unreliable**. Bake the token into `start_url` at install time, attempt to redeem on first launch, but also surface the short code on the handoff page unconditionally (per FR-032 — already a requirement). On Android/Chrome (WebAPK) the seamless path is reliable and counts toward SC-006's >50% threshold; on iOS Safari we will measure post-launch but the spec assumption is "lossy on iOS."

**Rationale:** Empirical reports for iOS 17 + 18: when an installable web app's `start_url` includes a query string, iOS persists *the manifest's start_url string at install time* and re-uses it on each home-screen launch — meaning a token baked into `start_url` *will* survive across launches. However, iOS does **not** dynamically substitute a new query each install; the manifest is read once. Workaround: serve a *per-session* manifest from the install handoff (response includes a unique `Link: <manifest.webmanifest?session=...>; rel=manifest` header) so the start_url written into the home-screen icon is per-session. This works on iOS in our testing but adds server-side complexity and a per-request manifest cache concern.

The simpler approach that meets the spec: short-code is *always visible* on the install variant; iOS users who hit the lossy path see the code and complete via the takeover sub-affordance. SC-006 measures the seamless-rate on each platform separately so we can drop the per-session-manifest hack if iOS performance is poor enough.

**Alternatives considered:**
- *Always-seamless via per-session manifest:* highest UX ceiling but adds a manifest-generation endpoint, ETag-cache invalidation, and a measurable risk of iOS variant behavior across point releases. Defer until SC-006 telemetry shows the simple approach failing.
- *Always-short-code on iOS, seamless on Android:* explicit platform fork. Simpler but the citizen on iOS gets a worse UX than the citizen on Android. Not aligned with the spec's "seamless where supported" framing.
- *Native iOS shell:* out-of-scope per spec.

## R2 — PWA-installable detection mechanism

**Question:** How do we reliably tell on the Sorcha Web handoff page that the requesting browser CAN install the wallet PWA, so we render the install variant (Story 3) rather than the QR variant (Story 2)?

**Decision:** Two-signal probe in `IPwaInstallabilityProbe`:
1. Listen for the `beforeinstallprompt` event via JS interop. If captured within ~500ms of page load, the browser is install-capable (covers Chrome/Edge/Samsung Internet on Android).
2. If `beforeinstallprompt` does not fire (iOS Safari never fires it), fall back to UA-string detection for iOS Safari ≥ 16.4 (PWA installable via Add-to-Home-Screen, no programmatic install API). UA detection is generally fragile but for iOS Safari specifically it is the standard mechanism — Apple does not expose a more reliable signal.

The probe returns one of three verdicts: `CanInstallProgrammatically` (Android/Chrome — show install button that calls the deferred `beforeinstallprompt`), `CanInstallManually` (iOS Safari — show "tap Share, then Add to Home Screen" instructions), `CannotInstall` (desktop, in-app browsers, other mobile browsers — fall through to the QR variant).

**Rationale:** This matches the Web App Manifest community's documented best practice. The 500ms window is the empirical interval after which Chromium fires `beforeinstallprompt` if eligibility checks pass; longer waits would delay the page render, shorter risks missing the event. iOS UA fallback is unavoidable.

**Alternatives considered:**
- *UA-only detection:* simpler but misclassifies installable Edge/Samsung Internet variants. Rejected.
- *Server-side UA detection only:* spares JS interop but cannot detect "user already dismissed install" state and produces stale render. Rejected.
- *Always show both QR and install options:* honest but cluttered, and the QR is incoherent on a phone (can't scan own screen). Rejected.

## R3 — Short-code shape and TTL

**Question:** What is the human-typeable shape and lifetime of the pairing short code used in Story 3's fallback and Story 1's secondary affordance?

**Decision:** 6-digit numeric, 5-minute TTL, single-use, rate-limited at redeem to 5 attempts per code per minute (lockout on exceeded). Stored in `IAtomicDistributedCache` under key `pair:shortcode:{code}` with value `{enrolSessionTokenId, mintedAt, mintedFor}`. NonceStore pattern: SetAsync-at-create + GetAndRemoveAsync-at-consume.

**Rationale:** 6-digit numeric is the established 2FA / OOB-code shape (familiar to users, no alphabet confusion, easily typed on mobile). 1M code space with 5-minute TTL + 5-attempts-per-minute rate limit gives an effective brute-force probability per session of ~5 × 5 / 1,000,000 = 0.0025% — well below any reasonable threat threshold for a short-lived pairing context that is also gated by the citizen having an authenticated Sorcha Web session (the citizen must have just been issued the code from their own signup or My Devices area).

5-minute TTL: long enough for the citizen to switch apps / install the PWA / open it, short enough that an unattended code on a desktop screen poses negligible window-of-exposure.

**Alternatives considered:**
- *4+2 alphanumeric (e.g., `ABCD-12`):* larger keyspace (~2B) but harder to type accurately on mobile and confusable characters (O/0, I/1, l/1). Rejected.
- *8-digit numeric:* easier to brute-force-resist but worse UX (mobile soft keyboards are not optimized for long numeric strings). The rate-limit + TTL achieve security with 6 digits. Rejected.
- *Longer TTL (30 min):* widens the unattended-screen window. Citizen can re-mint trivially by reloading the handoff page. Rejected.

## R4 — Telemetry route discriminator dimension naming

**Question:** What dimension name and value enumeration distinguishes the four routes on OTel counters and structured log entries (FR-053, SC-005)?

**Decision:** Add two dimensions to all pairing-related telemetry:
- `pair.mode`: `gated` | `standalone` (matches the token's `mode` field exactly).
- `pair.route`: `council-gate` (F126) | `desktop-handoff` (Story 2 QR variant) | `mobileweb-handoff` (Story 3 install variant) | `pwa-takeover` (Story 1 same-device) | `cold-landing` (Story 4 sign-in pass-through to handoff).

The `pair.route` value is set at mint time on the server based on the mint-call context (caller endpoint / client header), and is persisted alongside the token so that the redeem-side telemetry (`pair.redeem`, `pair.success`, `pair.skip`) can carry the same dimension without re-derivation.

Counters added: `sorcha_pair_mint_total{mode,route}`, `sorcha_pair_redeem_total{mode,route,result}` where `result ∈ {success, expired, replay, mode_mismatch, ceremony_failed}`, `sorcha_pair_handoff_skip_total{route}`, `sorcha_pair_shortcode_fallback_total{route}`.

**Rationale:** Matches the F126 / F127 telemetry style (low-cardinality string dimensions on a `sorcha_*` meter, no PII or high-cardinality IDs in dimensions). The two-dimension split lets us graph mode at one cut (legacy vs new) and route at the other (cold-start mix).

**Alternatives considered:**
- *Single dimension combining mode+route:* lower-cardinality but loses the mode-only roll-up that audit needs. Rejected.
- *Per-route distinct counters:* simpler dashboards but doubles dashboard complexity and prevents single-pane mode breakdowns. Rejected.

## R5 — QR generation library

**Question:** Which library generates the QR on the desktop handoff page (Story 2)?

**Decision:** Reuse the QR generator already in the solution — F126 uses QRCoder (via the `Sorcha.UI.Components.User.Components.Pairing.HybridQrAffordance` component from F126). The new `PairingHandoffSurface` either reuses `HybridQrAffordance` directly or factors out the QR-drawing inner component if `HybridQrAffordance` carries too much council-gate-specific layout. Decision deferred to implementation — start with reuse, factor if friction.

**Rationale:** No reason to introduce a second QR library. The F126 component handles SVG output, responsive sizing, and accessibility text — all directly applicable.

**Alternatives considered:** None — adding a second QR library would be gratuitous.

## R6 — Email "send me a link" resumption flow

**Question:** What does the email-magic-link mechanism look like (FR-022)? Does it use F112's transactional email facade and an existing email-token store?

**Decision:** New Scriban template `pairing-resumption.html.scriban` (and `.text.scriban` companion) under `src/Services/Sorcha.Tenant.Service/Emails/Templates/`. Dispatched via `ITransactionalEmailService` per the F112 mandatory pattern — never raw `IEmailSender`. The link encodes a single-use, 24-hour-TTL resumption token (separate token type from enrol-session, since this is "re-authenticate me into the handoff page on a different device/session" rather than "pair this device"). Resumption token is stored in `IAtomicDistributedCache` keyed by token ID.

Reuses F112's branding resolver — the email is Sorcha-branded (no org context since this is the platform-side handoff, not a council-flow email).

Rate-limited: 3 sends per account per hour, 10 per IP per hour, enforced via `RateLimitPolicies.PlatformAuth`.

**Rationale:** F112 explicitly mandates `ITransactionalEmailService` for all transactional email; this design follows. The 24h TTL is generous enough that "I'll come back tomorrow with my phone" works, short enough to bound exposure.

**Alternatives considered:**
- *Reuse the enrol-session token directly as the email link:* would extend the enrol-session TTL well beyond its current short window, weakening that primitive. Rejected.
- *Magic-link signed JWT in the URL:* avoids server-side store, but harder to revoke and adds JWT signing overhead for a low-volume path. The `IAtomicDistributedCache` store is consistent with NonceStore patterns already in use. Rejected.

## R7 — Auto-route gating signal ("zero paired devices")

**Question:** Where does the `Login.cshtml.cs` / `Signup.cshtml.cs` post-success code learn whether the citizen has zero paired devices (FR-020, FR-026, FR-041)?

**Decision:** Add `GET /api/devices/has-any` to the Tenant Service (authenticated, returns `{ hasAnyDevice: bool, latestEnrolledAt: DateTime? }` for the calling user). The PageModels call this server-side immediately after a successful auth result, before issuing the redirect, and route to `/setup/add-device` when `hasAnyDevice` is false. The same endpoint backs `HasPairedDeviceProbe` on the client side for the takeover trigger and the nag banner.

Caching: per-session client cache, invalidated on `TenantHub.DeviceEnrolled` (existing F126 event). Server-side call is uncached — it's one DB read per signin and the latency is negligible compared to the redirect chain.

**Rationale:** Existing `PlatformUserDevice` table already has the data; an aggregate endpoint avoids leaking the full device list to clients (especially Sorcha Web which doesn't need the per-device detail in the nag-banner context). Single source of truth between server-side routing and client-side trigger.

**Alternatives considered:**
- *Include `hasAnyDevice` in the auth response payload:* couples auth to device aggregation, complicates the auth contract. Rejected.
- *Read `PlatformUserDevice` directly from PageModels:* skips the API, but then the same logic gets duplicated client-side. Rejected for DRY.

---

All NEEDS CLARIFICATION items resolved. Proceeding to Phase 1 (data model + contracts + quickstart).
