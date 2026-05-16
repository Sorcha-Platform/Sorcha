---
description: "Task list for feature 128 — Cold-start onboarding and device pairing UX"
---

# Tasks: Cold-start onboarding and device pairing UX

**Input**: Design documents from `/specs/128-cold-start-onboarding/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED. The Sorcha constitution mandates >85% coverage on new code (Principle IV). Each user story's bunit / xUnit / Playwright test tasks are part of that story's phase.

**Organization**: Phases 3–6 each correspond to one user story (US1 → US4 in priority order). Each story is independently testable and shippable.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story label for Phase 3–6 tasks only
- Paths use existing Sorcha solution layout per plan.md

## Path Conventions

This feature uses the existing Sorcha microservices web layout:
- `src/Services/Sorcha.Tenant.Service/` — backend (all new endpoints)
- `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/` — shared Blazor components
- `src/Apps/Sorcha.UI.Web/` and `src/Apps/Sorcha.UI.Web.Client/` — web host + WASM client
- `src/Apps/Sorcha.Wallet.Pwa/` — wallet PWA
- `tests/` — mirrored project-test pairs

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify baseline + prepare cross-cutting registrations. No code changes required to Aspire or Docker — this feature reuses existing service hosts.

- [ ] T001 Verify branch `128-cold-start-onboarding` builds clean against current master baseline: `dotnet build` exits 0 and all existing tests (Tenant 1084+/1092, UI.Core 1192/1192, Wallet.Pwa 111/111) pass with no regressions.
- [ ] T002 Walk the brainstorm + design memo + spec + plan + research + data-model + contracts + quickstart in sequence to anchor context. Files: `docs/superpowers/specs/2026-05-16-cold-start-onboarding-design.md`, `specs/128-cold-start-onboarding/{spec,plan,research,data-model,quickstart}.md`, `specs/128-cold-start-onboarding/contracts/*.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Token-primitive extension + short-code transport + has-any aggregate + telemetry counters + Enrol.razor copy variants. Every user story depends on these. Must complete in full before any Phase 3+ work.

**⚠️ CRITICAL**: No US1–US4 work can begin until this phase is complete.

### Token primitive extension (FR-001..FR-004)

- [ ] T003 Extend `EnrolSessionMintRequest` DTO with optional `Mode { Gated, Standalone }` enum (default `Gated`) in `src/Services/Sorcha.Tenant.Service/Models/EnrolSessionDtos.cs`. Existing F126 callers continue to work without sending `mode`.
- [ ] T004 Extend `EnrolSession` cache record + `EnrolSessionService.MintAsync` to persist `Mode` and `Route` (string, low-cardinality from research R4) in `src/Services/Sorcha.Tenant.Service/Services/EnrolSessionService.cs`. Default `Route = "council-gate"` for back-compat.
- [ ] T005 Add mint-time mode/context enforcement to `EnrolSessionService.MintAsync`: reject `mode=gated` without `returnTo` and `mode=standalone` with `returnTo` (400 `mode-context-mismatch`).
- [ ] T006 Extend `EnrolSessionRedeemResponse` DTO to echo `Mode` and `ReturnTo` (null on standalone) in `src/Services/Sorcha.Tenant.Service/Models/EnrolSessionDtos.cs`.
- [ ] T007 Update `EnrolSessionEndpoints.cs` `POST /api/auth/enrol-session` and `/redeem` to wire the new fields + return-shape per `contracts/rest-endpoints.md` in `src/Services/Sorcha.Tenant.Service/Endpoints/EnrolSessionEndpoints.cs`. Update `.WithSummary()` / `.WithDescription()` for Scalar.
- [ ] T008 [P] Add xUnit `EnrolSessionServiceModeTests` covering: mode persistence, gated-without-returnTo rejection, standalone-with-returnTo rejection, mode echo on redeem, immutability of mode post-mint, in `tests/Sorcha.Tenant.Service.Tests/Services/EnrolSessionServiceModeTests.cs`.

### Short-code transport (FR-013, FR-033, FR-050, FR-051)

- [ ] T009 Add `PairingShortCodeDtos.cs` to `src/Services/Sorcha.Tenant.Service/Models/` per `data-model.md` (mint request + response, redeem request, error shapes).
- [ ] T010 Add `PairingShortCodeService` in `src/Services/Sorcha.Tenant.Service/Services/PairingShortCodeService.cs`: 6-digit numeric mint with uniform random + 3-retry collision check, `IAtomicDistributedCache` SetAsync with 5-min TTL keyed `pair:shortcode:{code}`, `GetAndRemoveAsync` redeem, per-code attempt counter at `pair:shortcode:{code}:attempts` with 5/min lockout.
- [ ] T011 Add `PairingShortCodeEndpoints.cs` for `POST /api/auth/enrol-session/short-code` (authenticated) and `POST /api/auth/enrol-session/redeem-short-code` (authenticated) in `src/Services/Sorcha.Tenant.Service/Endpoints/`. Wire `RateLimitPolicies.PlatformAuth` on the mint; per-code attempt counter on redeem. Scalar metadata + XML docs.
- [ ] T012 Wire DI for `PairingShortCodeService` in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs` and map endpoints in `Program.cs`.
- [ ] T013 [P] Add xUnit `PairingShortCodeServiceTests` covering: 6-digit numeric shape, TTL, single-use enforcement via NonceStore pattern, collision retry, attempt-rate-limit lockout, underlying-session is standalone-mode-only, in `tests/Sorcha.Tenant.Service.Tests/Services/PairingShortCodeServiceTests.cs`.
- [ ] T014 [P] Add `PairingShortCodeEndpointsTests` (WebApplicationFactory integration) covering mint auth + rate limit + replay + expired + 429 attempt lockout, in `tests/Sorcha.Tenant.Service.Tests/Endpoints/PairingShortCodeEndpointsTests.cs`.

### Has-any-device aggregate (FR-010, FR-020, FR-024, FR-026, FR-041)

- [ ] T015 Add `DeviceAggregateEndpoints.cs` for `GET /api/devices/has-any` (authenticated) in `src/Services/Sorcha.Tenant.Service/Endpoints/`. Reads `PlatformUserDevice` via the existing F114 service interface. Returns `{ hasAnyDevice, latestEnrolledAt }`.
- [ ] T016 [P] Add `DeviceAggregateEndpointsTests` covering: unauthenticated 401, zero-device user, paired-device user with `latestEnrolledAt`, ignores revoked devices, in `tests/Sorcha.Tenant.Service.Tests/Endpoints/DeviceAggregateEndpointsTests.cs`.

### Shared `HasPairedDeviceProbe` client service (FR-010, FR-024)

- [ ] T017 Add `IHasPairedDeviceProbe` + `HasPairedDeviceProbe` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Pairing/`. Calls `GET /api/devices/has-any`, caches per-session, exposes a `Changed` event, subscribes to `TenantHubConnection.OnDeviceEnrolled` to invalidate + republish.
- [ ] T018 Add a `RaiseLocalPairCompleted()` method on the probe + subscription path so PWA-side same-device pair-success can trigger invalidation without waiting for the hub round-trip (covers FR-014's "local" cause).
- [ ] T019 [P] Add `HasPairedDeviceProbeTests` (bunit + service-level) covering: initial fetch, cache hit, hub-event invalidation, local-event invalidation, race between hub-event and local-event, in `tests/Sorcha.UI.Components.User.Tests/Services/User/Pairing/HasPairedDeviceProbeTests.cs`.

### Enrol.razor mode-aware copy (FR-002, in service of US2/US3 redeem UX)

- [ ] T020 Update `src/Apps/Sorcha.Wallet.Pwa/Pages/Enrol.razor` to read `mode` from the redeem response and render two copy variants: `gated` keeps today's "we'll bring you back" message + redirects to `returnTo`; `standalone` renders "You're being set up" + redirects to PWA Home on success. Reject standalone tokens that arrive with a `returnTo` defensively.
- [ ] T021 [P] Add `EnrolModeCopyTests` (bunit) for the two copy variants + returnTo-ignored-on-standalone path, in `tests/Sorcha.Wallet.Pwa.Tests/Pages/EnrolModeCopyTests.cs`.

### Telemetry counters (FR-053, SC-005, SC-007)

- [ ] T022 Add the counters listed in `contracts/telemetry-events.md` to the existing `sorcha` OTel meter registration in `src/Services/Sorcha.Tenant.Service/Extensions/TelemetryExtensions.cs` (or wherever the existing F126 enrol-session counters are registered). Names + dimensions match the contract exactly. Wire emissions into `EnrolSessionService` (mint, redeem) and `PairingShortCodeService` (mint, redeem).
- [ ] T023 [P] Add `EnrolSessionTelemetryTests` verifying counter increments + dimensions on mint and redeem paths in `tests/Sorcha.Tenant.Service.Tests/Telemetry/EnrolSessionTelemetryTests.cs`.

**Checkpoint**: Foundation ready. T020 also unblocks any partial council-gate regression check via existing F126 E2E suites; rerun those to verify no regression before starting Phase 3.

---

## Phase 3: User Story 1 — In-PWA pairing takeover (Priority: P1) 🎯 MVP

**Goal**: A signed-in citizen who opens the wallet PWA with zero paired devices on this hardware sees a full-page takeover, completes the device-pairing ceremony in place, and lands on a working wallet.

**Independent Test**: Provision a citizen with zero paired devices, sign in to a fresh PWA install, confirm the takeover appears, complete the pair action, confirm the takeover dismisses and the device appears in My Devices.

### Component + wiring

- [ ] T024 [US1] Create `PairingTakeover.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Pairing/PairingTakeover.razor`. Full-screen overlay (outside `MudContainer`, sibling to `WelcomeTakeover`). Primary "Set up" button invokes the existing F114 device-pairing ceremony with the current PWA session. Secondary disclosable affordance "Already started on another device?" with a 6-digit code input that posts to `POST /api/auth/enrol-session/redeem-short-code`.
- [ ] T025 [US1] Mount `PairingTakeover` in `src/Apps/Sorcha.Wallet.Pwa/Shared/MainLayout.razor` conditional on `IUserContext.IsAuthenticated && !IHasPairedDeviceProbe.HasAnyDevice`. Outside `MudContainer`, before any other content.
- [ ] T026 [US1] Wire takeover auto-dismiss: subscribe `PairingTakeover` to `IHasPairedDeviceProbe.Changed`; dismiss when `HasAnyDevice` flips to true. Covers FR-014 (local + remote pair-success).
- [ ] T027 [US1] Wire takeover navigation blocking: ensure the overlay z-index + `pointer-events` cover the MainLayout's navigation regions; document the wiring in a one-line code comment.
- [ ] T028 [US1] Wire takeover render telemetry: emit `sorcha_pair_takeover_render_total{result}` from the takeover's `OnInitializedAsync`, emit `sorcha_pair_takeover_dismissed_total{cause}` from the dismiss path with the right cause discriminator.
- [ ] T029 [US1] Add DI registration for `IHasPairedDeviceProbe` in `src/Apps/Sorcha.Wallet.Pwa/Program.cs` (was T017's home; if not registered there from Foundational, register here).

### Tests

- [ ] T030 [P] [US1] Add `PairingTakeoverTests` (bunit) in `tests/Sorcha.UI.Components.User.Tests/Pairing/PairingTakeoverTests.cs`: renders on zero-device, hidden on paired, primary-button invokes pair-ceremony delegate, secondary short-code input redeems via injected client, hub-event dismissal, local-event dismissal.
- [ ] T031 [P] [US1] Add Playwright E2E `PwaUnpairedTakeoverE2E.cs` in `tests/Sorcha.UI.E2E/ColdStartOnboarding/`: happy path (sign-in → takeover → set-up → dismiss → wallet visible); remote-pair dismissal across two browser contexts.

**Checkpoint**: US1 fully functional. The operator-flagged irritant (Settings → Enrol being the only path) is resolved end-to-end.

---

## Phase 4: User Story 2 — Desktop → phone handoff (Priority: P1)

**Goal**: After first signup on Sorcha Web (or sign-in with zero paired devices), the citizen is routed to a full-page handoff with QR + email-link + skip. Scan-and-pair completes the wallet on the phone within seconds; skip lands the citizen on the web with a persistent banner + menu entry that both return to the handoff.

**Independent Test**: From a desktop browser, fresh-signup a citizen; confirm `/setup/add-device` appears; scan the QR with a phone; confirm pairing succeeds on both surfaces. Repeat with Skip and confirm the banner + menu entry both link back to the handoff.

### Handoff surface

- [ ] T032 [US2] Create `PairingHandoffSurface.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Pairing/PairingHandoffSurface.razor`. Default renders the QR variant: mints `mode=standalone, route=desktop-handoff` token via injected client, displays QR via the existing F126 QR generator (reuse `HybridQrAffordance` or factored inner SVG component per research R5), shows "Email me a link" and "Skip for now" affordances.
- [ ] T033 [US2] Create `/setup/add-device` page: `src/Apps/Sorcha.UI.Web/Pages/Setup/AddDevice.cshtml` (+ `.cshtml.cs`). Authenticated. Hosts `PairingHandoffSurface`. PageModel checks the `has-any-device` aggregate; if user already has ≥1 paired device redirects to `/devices` (FR-026 belt-and-braces; the auto-route gate in T035/T036 should already prevent this).
- [ ] T034 [US2] Wire `PairingHandoffSurface` "Skip for now" → navigate to `/devices` (or web wallet home if no devices area) AND signal the nag-banner to display via a shared session-state service.

### Post-signup auto-routing (FR-020, FR-026)

- [ ] T035 [US2] Extend `src/Apps/Sorcha.UI.Web/Pages/Signup.cshtml.cs` post-success path: after successful signup, call `GET /api/devices/has-any` server-side; if `hasAnyDevice == false`, redirect to `/setup/add-device` instead of the returnUrl. Preserve the returnUrl into a session-state slot so the handoff's later "Skip" or "completed pair" can route there.
- [ ] T036 [US2] Extend `src/Apps/Sorcha.UI.Web/Pages/Login.cshtml.cs` post-success path: same gate as T035 for citizens with zero paired devices. Existing returnUrl semantics preserved for citizens with paired devices.

### Persistent nag banner + menu entry (FR-024, FR-025)

- [ ] T037 [US2] Create `PairingNagBanner.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Pairing/PairingNagBanner.razor`. Renders when `IHasPairedDeviceProbe.HasAnyDevice == false`. Dismissable per-session. CTA routes to `/setup/add-device`.
- [ ] T038 [US2] Mount `PairingNagBanner` at the top of the Sorcha Web layout: `src/Apps/Sorcha.UI.Web.Client/Shared/MainLayout.razor` (or the equivalent client-side layout used by the wallet/management area).
- [ ] T039 [US2] Add "Add my phone" menu entry to the existing F114 My Devices area: locate the devices page (likely under `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Devices/` or `src/Apps/Sorcha.UI.Web.Client/Pages/`) and add an entry that navigates to `/setup/add-device`. Entry visible regardless of paired-device count (per FR-025).

### Email "send me a link" resumption (FR-022)

- [ ] T040 [US2] Add `PairingResumptionTokenService` in `src/Services/Sorcha.Tenant.Service/Services/PairingResumptionTokenService.cs`: mint (24h TTL, `IAtomicDistributedCache` keyed `pair:resumption:{id}`), redeem (`GetAndRemoveAsync`, returns userId for session re-establishment).
- [ ] T041 [US2] Add `POST /api/auth/pairing-resumption-email` endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/PairingResumptionEndpoints.cs` (authenticated, 3/account/hour + 10/IP/hour rate limits). Mints a resumption token, dispatches the email via `ITransactionalEmailService` (F112 facade — never raw `IEmailSender`).
- [ ] T042 [US2] Add `GET /api/auth/pairing-resumption/redeem?token={id}` endpoint in the same file (anonymous). Redeems the resumption token, re-establishes a Sorcha session for the resolved userId, 302-redirects to `/setup/add-device` (or `/login?reason=resumption-expired` on failure).
- [ ] T043 [US2] Add Scriban template `pairing-resumption.html.scriban` (+ `.text.scriban` companion) under `src/Services/Sorcha.Tenant.Service/Emails/Templates/`. Sorcha-branded only (no per-org branding for this flow). Carries the resumption URL.
- [ ] T044 [US2] Add `IPairingResumptionDispatch` record + `SendPairingResumptionAsync` method on `ITransactionalEmailService` and its implementation. Snapshot fixture committed at `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/pairing-resumption.html` + `.text` (regenerate with `UPDATE_EMAIL_FIXTURES=1`).

### Tests

- [ ] T045 [P] [US2] `PairingHandoffSurfaceTests` (bunit) in `tests/Sorcha.UI.Components.User.Tests/Pairing/PairingHandoffSurfaceTests.cs`: QR variant renders, mint-on-load, Skip dispatches nag-banner signal + navigates, email-link button calls endpoint.
- [ ] T046 [P] [US2] `PairingNagBannerTests` (bunit): shows on zero-device, hides on paired, dismiss-per-session, CTA navigates to `/setup/add-device`. In `tests/Sorcha.UI.Components.User.Tests/Pairing/PairingNagBannerTests.cs`.
- [ ] T047 [P] [US2] `SetupAddDevicePageModelTests` + `SignupPostSuccessRoutingTests` + `LoginPostSuccessRoutingTests`: cover the gate (zero-device → /setup/add-device, paired → returnUrl). In `tests/Sorcha.UI.Web.Tests/PageModels/`.
- [ ] T048 [P] [US2] `PairingResumptionTokenServiceTests` + `PairingResumptionEndpointsTests`: mint shape, TTL, single-use, rate-limit, redeem re-establishes session + 302s. In `tests/Sorcha.Tenant.Service.Tests/Services/` + `Endpoints/`.
- [ ] T049 [P] [US2] Email-template snapshot test for `pairing-resumption` rendering in `tests/Sorcha.Tenant.Service.Tests/Emails/EmailTemplateSnapshotTests.cs` (extend existing test class).
- [ ] T050 [P] [US2] Playwright E2E `DesktopToPhoneHandoffE2E.cs` in `tests/Sorcha.UI.E2E/ColdStartOnboarding/`: signup → handoff appears → QR mint visible; skip path → banner visible → click → handoff returns; Add-my-phone menu entry → handoff opens.

**Checkpoint**: US2 fully functional. Desktop signups now reach a paired-phone state in one continuous flow; skip-then-return is friction-light.

---

## Phase 5: User Story 3 — Mobile-web → same-phone PWA install (Priority: P2)

**Goal**: A mobile-web signup detects PWA-installability and renders an install-flavoured handoff. Where possible the wallet opens already paired; where lossy, the citizen completes via the takeover's short-code sub-affordance (already shipped in US1).

**Independent Test**: From a mobile browser, fresh-signup → handoff renders the install variant with the short code always visible → install → wallet opens either paired (seamless) or to the takeover (fallback) where the short code completes pairing.

### Installability probe + variant rendering

- [ ] T051 [US3] Add `IPwaInstallabilityProbe` + `PwaInstallabilityProbe` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Pairing/`. JS interop captures `beforeinstallprompt` within ~500ms; falls back to iOS Safari ≥16.4 UA detection per research R2. Verdict enum: `CanInstallProgrammatically`, `CanInstallManually`, `CannotInstall`.
- [ ] T052 [US3] Add JS interop module `wwwroot/js/pwa-install-probe.js` (or extend the existing PWA JS file) holding the deferred `beforeinstallprompt` and exposing a `.prompt()` method for `CanInstallProgrammatically` paths.
- [ ] T053 [US3] Extend `PairingHandoffSurface.razor` to consult `IPwaInstallabilityProbe` on render: render install-flavoured variant when verdict is `CanInstallProgrammatically` or `CanInstallManually`. Variant displays "Install Sorcha Wallet" (or iOS Add-to-Home-Screen instructions) and ALWAYS shows the 6-digit short code below the install affordance (FR-032).
- [ ] T054 [US3] On the install variant, mint a short code via `POST /api/auth/enrol-session/short-code` with `route="mobileweb-handoff"`. Display the code prominently. Re-mint on TTL expiry with a visible countdown / refresh affordance.

### Seamless `start_url`-baked token path

- [ ] T055 [US3] On `CanInstallProgrammatically` invoke, bake the underlying enrol-session token into the install URL (start_url `?session=<token>`). The PWA on first launch reads `?session=` from `NavigationManager.Uri`, attempts redeem, and on success skips the takeover and lands on Home.
- [ ] T056 [US3] Wire the PWA boot path: `src/Apps/Sorcha.Wallet.Pwa/Program.cs` (or `App.razor` / `MainLayout.razor`) detects `?session=` on first navigation, calls the existing redeem endpoint, falls through to the takeover on any failure. Emits `sorcha_pair_redeem_total{route=mobileweb-handoff, result=...}` with the right `result`.
- [ ] T057 [US3] On the iOS path (`CanInstallManually`), the install URL cannot be made per-session via the standard manifest. Document this in inline code commentary and rely on the short-code fallback per research R1.

### Tests

- [ ] T058 [P] [US3] `PwaInstallabilityProbeTests` (bunit + JS interop test double): `beforeinstallprompt` captured → `CanInstallProgrammatically`; no event + iOS UA → `CanInstallManually`; no event + non-iOS UA → `CannotInstall`. In `tests/Sorcha.UI.Components.User.Tests/Services/User/Pairing/PwaInstallabilityProbeTests.cs`.
- [ ] T059 [P] [US3] Extend `PairingHandoffSurfaceTests` (bunit) with the install-variant render path: install button visible, short code visible without interaction, short code re-mints on TTL.
- [ ] T060 [P] [US3] `WalletPwaSeamlessRedeemTests` (bunit) verifying first-launch `?session=` is consumed, redeemed, and the takeover is bypassed on success; on failure, takeover renders with `result` telemetry. In `tests/Sorcha.Wallet.Pwa.Tests/Pages/WalletPwaSeamlessRedeemTests.cs`.
- [ ] T061 [P] [US3] Playwright E2E `MobileWebInstallHandoffE2E.cs` in `tests/Sorcha.UI.E2E/ColdStartOnboarding/`: mobile-browser context (UA + viewport emulation), signup → install variant + short code visible; if Chromium-Android (install-capable): install + seamless paired; otherwise short-code fallback through the takeover.

**Checkpoint**: US3 fully functional on both seamless (Android/Chromium) and fallback (iOS Safari) platforms.

---

## Phase 6: User Story 4 — App-store cold landing (Priority: P3)

**Goal**: An unauthenticated visitor at `sorcha.dev/get` sees what Sorcha is, can find a service that uses it, or sign in. Sign-in routes a zero-device citizen into the US2/US3 handoff.

**Independent Test**: Visit `/get` unauthenticated → landing renders; click sign-in as a zero-device citizen → land on `/setup/add-device`.

- [ ] T062 [US4] Create `src/Apps/Sorcha.UI.Web/Pages/Get.cshtml` (+ `.cshtml.cs`). Anonymous. Renders: Sorcha explainer copy, "Find services that use Sorcha" link (single-service: Strathcarron, hard-coded per spec Assumption), "I already have an account → sign in" CTA.
- [ ] T063 [US4] Wire the sign-in CTA: routes to existing Sorcha Web sign-in with a `returnUrl` indicating cold-landing origin so the post-success router (T036) can attribute the `cold-landing` route dimension on the next mint. Specifically, the post-success path emits a token-mint with `route=cold-landing` rather than `desktop-handoff` for citizens entering through `/get`.
- [ ] T064 [US4] Verify the cold-landing path appears on the marketing landing's link target (`sorcha.dev/get`) — at minimum a comment in `Get.cshtml` documenting that the deployed routing is via the existing nginx + sorcha-ui-web mapping and no new routing config is required.
- [ ] T065 [P] [US4] `GetLandingTests` (bunit) in `tests/Sorcha.UI.Web.Tests/Pages/GetLandingTests.cs`: unauthenticated render, sign-in CTA links correctly with the cold-landing returnUrl.
- [ ] T066 [P] [US4] Extend `LoginPostSuccessRoutingTests` (T047) with one additional case: a citizen entering via the cold-landing returnUrl who has zero paired devices is routed through `/setup/add-device` with the `route=cold-landing` dimension on the subsequent mint.
- [ ] T067 [P] [US4] Playwright E2E `ColdLandingE2E.cs` in `tests/Sorcha.UI.E2E/ColdStartOnboarding/`: unauthenticated `/get` renders; sign-in flow lands at `/setup/add-device` for a zero-device account.

**Checkpoint**: All four user stories independently functional. The full cold-start surface is shipped.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation sync, observability dashboards, quickstart verification, memory + skill updates.

- [ ] T068 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` with a new "Feature 128 — Cold-start onboarding" section: token primitive extension, four routes, has-any-device aggregate, short-code transport, telemetry counters.
- [ ] T069 [P] Update `docs/reference/API-DOCUMENTATION.md` with the new endpoints (mode field on enrol-session, short-code mint/redeem, devices/has-any, pairing-resumption send/redeem).
- [ ] T070 [P] Update `CLAUDE.md` Feature API References paragraph to mention F128 alongside the existing F114/F116/F124/F125/F126/F127 references.
- [ ] T071 [P] Update `.specify/MASTER-TASKS.md`: mark F128 phases as they complete; do not let this skip behind real progress.
- [ ] T072 [P] Update `src/Services/Sorcha.Tenant.Service/README.md` with the new endpoint surface and short-code lifecycle notes.
- [ ] T073 [P] Update `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/README.md` Components.User folder listing with `Pairing/` and `Services/User/Pairing/`.
- [ ] T074 Walk `specs/128-cold-start-onboarding/quickstart.md` end-to-end against a fresh local docker-compose stack; correct any drift between the quickstart and actual behaviour discovered during the walk.
- [ ] T075 Build a Grafana / Aspire dashboard query set for the counters in `contracts/telemetry-events.md`: per-route mint vs redeem funnel, skip rate, short-code fallback rate per platform, mode mismatch counter (must stay at 0 successes per SC-007). Capture queries as text in `specs/128-cold-start-onboarding/observability.md` (new file) for the operator to deploy later.
- [ ] T076 Verify the F126 council-gate baseline regression budget (SC-008): rerun the existing F126 walkthrough + E2E suite; confirm pass + no telemetry shift on `route=council-gate` baseline metrics.
- [ ] T077 Final regression: `dotnet test` across the whole solution; resolve any new failures introduced by this feature; confirm Tenant + UI.Core + Wallet.Pwa + new E2E counts.
- [ ] T078 Open PR `128-cold-start-onboarding` → `master`; wait for `claude-review`; do not self-merge before review per the standing instruction.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: T001 immediate. T002 is the brief.
- **Phase 2 (Foundational)**: BLOCKS Phases 3–6. All of T003–T023 must complete before any US-tagged task begins. Within Phase 2, T003–T007 are sequential (each builds on the previous DTO/service shape); T008 [P] can run alongside any later Foundational task. T009–T014 are an internal block (short-code primitive). T015–T019 are an internal block (has-any aggregate + probe). T020–T021 are an internal block (Enrol.razor copy). T022–T023 layer on top of the service implementations.
- **Phase 3 (US1 / P1 MVP)**: Depends only on Foundational. T024–T029 are mostly sequential within the file; tests T030–T031 run after the impl tasks they cover.
- **Phase 4 (US2 / P1)**: Depends on Foundational. Can run in parallel with Phase 3 once Foundational is complete. T032 + T033 are sequential (surface before page); T035 + T036 depend on the has-any endpoint (T015) from Foundational; T037 + T038 depend on `HasPairedDeviceProbe` (T017); T039 is self-contained on the existing devices page; T040–T044 are the email subsystem (sequential within the subsystem); T045–T050 are tests for the above.
- **Phase 5 (US3 / P2)**: Depends on Foundational AND on Phase 4's `PairingHandoffSurface` existing (T032). T051–T054 are the probe + variant + short-code mint wiring; T055–T057 are the PWA boot-side seamless redeem; T058–T061 are tests.
- **Phase 6 (US4 / P3)**: Depends on Foundational AND on Phase 4 (specifically T036 — the post-success router must already understand `cold-landing` as an attribution input). T062–T064 sequential; T065–T067 are tests.
- **Phase 7 (Polish)**: Depends on completion of every Phase that will ship in the PR. T076 + T077 must run after the per-story tests so they catch integration regressions; T078 last.

### User story dependencies (post-Foundational)

- **US1 (P1)**: independent of US2/US3/US4 once Foundational is done.
- **US2 (P1)**: independent of US1, US3, US4 once Foundational is done.
- **US3 (P2)**: depends on US2's `PairingHandoffSurface` shell existing (T032). Without US2, US3 has no surface to render an install variant on.
- **US4 (P3)**: depends on US2's `Login.cshtml.cs` router (T036) understanding cold-landing attribution.

### Within each user story

- Component tasks (T024 for US1, T032 for US2, T051 for US3, T062 for US4) before tests for that component.
- Page-model tasks before page-model tests.
- Endpoint tasks (Foundational already covered; US2 adds email endpoints) before endpoint tests.
- E2E test last within the story to catch wiring gaps the unit tests miss.

### Parallel opportunities

- **Within Foundational**:
  - T008 [P] can run alongside T009–T014 work.
  - T013 [P] and T014 [P] can run parallel to each other and to T015–T019.
  - T016 [P] and T019 [P] are independent.
  - T021 [P] independent of T020-impl by virtue of being the test.
  - T023 [P] independent.
- **Across Phases 3–6**: once Foundational completes, US1 and US2 implementation can run in parallel by different operators / agents. US3 and US4 follow after their respective US2 dependencies.
- **Within US1**: T030 [P] and T031 [P] parallel after T024–T029.
- **Within US2**: T045–T050 are all [P] across different files and projects.
- **Within US3**: T058–T061 are all [P].
- **Within US4**: T065–T067 are all [P].
- **Polish phase**: T068–T073 are all [P] (doc edits in separate files); T074–T077 are sequential validation steps; T078 is the gate.

---

## Implementation Strategy

### MVP path

**Ship US1 alone first** (Phases 1 + 2 + 3 + a slim Phase 7 that only covers T074 / T076 / T077 / T078). This delivers the operator's flagged irritant fix end-to-end. The takeover's short-code sub-affordance works against the Foundational short-code endpoints, even though no other story uses them yet. SC-001 + SC-004 are achievable in this MVP slice.

### Incremental delivery

After the MVP merges, ship US2 → US3 → US4 in three follow-on sub-PRs (matching the F124/F125/F126 sub-PR pattern). Each sub-PR is independently demoable:

- Sub-PR A (MVP): Foundational + US1
- Sub-PR B: US2 (desktop-handoff + nag-banner + Add-my-phone + email-resumption)
- Sub-PR C: US3 (installability probe + install variant + seamless redeem)
- Sub-PR D: US4 (cold landing) + Phase 7 polish + tag `spec-128-complete`

This keeps each sub-PR under ~30 files, tractable for `claude-review`, and continues to deliver visible operator value at each merge.

### Risk monitoring during implementation

- **Mode discriminator drift (R3 risk):** verify SC-007's mode-mismatch counter is zero after the first week post-MVP — any non-zero success entry there is an incident.
- **iOS seamless rate (R1 risk):** measure SC-006 after US3 ships; if the seamless path's success rate on iOS sits below 20%, drop the per-session manifest hack and document iOS as always-short-code in `quickstart.md`.
- **Skip-then-never-pair (SC-003 risk):** measure after US2 ships; if rate is below 60% (well below the 80% target), revisit whether the desktop handoff should also be non-dismissable.

---

## Parallel Execution Examples

### Foundational kick-off (after T003–T007 land):

```text
Operator A: T008 (test EnrolSessionServiceMode)
Operator B: T009 → T010 → T011 → T012 (short-code subsystem)
Operator C: T015 (has-any endpoint) → T016 (test) → T017 (probe) → T019 (probe test)
Operator D: T020 (Enrol.razor mode copy) → T021 (test)
Operator E: T022 (telemetry counters) → T023 (test)
```

### Phase 3 + Phase 4 in parallel post-Foundational:

```text
Operator A: T024 → T025 → T026 → T027 → T028 → T029 → T030 (US1)
Operator B: T032 → T033 → T034 → T035 → T036 → T037 → T038 → T039 (US2 main path)
Operator C: T040 → T041 → T042 → T043 → T044 (US2 email subsystem)
```

### Polish phase paralellism:

```text
T068, T069, T070, T071, T072, T073 — all [P] doc edits in different files
T074 → T075 → T076 → T077 → T078 — sequential validation gate
```

---

## Test summary

- **Unit / service-level**: T008, T013, T016, T019, T021, T023, T030, T045, T046, T047, T048, T049, T058, T059, T060, T065, T066 — 17 test tasks across bunit + xUnit.
- **Integration (WebApplicationFactory)**: T014, T048 — 2 tasks.
- **Telemetry**: T023 — 1 task.
- **E2E (Playwright)**: T031, T050, T061, T067 — 4 tasks, one per user story.
- **Total task count**: 78.
- **Per-story task counts**: Setup 2, Foundational 21, US1 8, US2 19, US3 11, US4 6, Polish 11.

## Format validation

Every task line begins with `- [ ] T### [P?] [Story?] description with file path.` Setup, Foundational, and Polish phases carry no [Story] label. Phases 3–6 carry [US1]–[US4] respectively. File paths use the existing Sorcha solution layout from plan.md.
