# PWA / mobile-app feedback backlog (2026-06-25)

Triaged from live mobile-app feedback. Each item is a candidate **separate** prodexec run
(prodexec thrashes on bundles). Confidence + whether live confirmation is needed is marked per item.
Part of the broader [PWA UI fixes initiative]. Camera-first (Present) and Verify-unification are
tracked elsewhere; this is the new bug/feature batch.

---

## 1. Persona "My Profile" does not save  — and can it autofill x-persona?
**Status:** root cause HIGH confidence. Two parts.

**a) Save is not wired in the PWA (feature gap, not a bug).**
- `src/Apps/Sorcha.Wallet.Pwa/Pages/Profile.razor` is a **stub** ("Per-context profile editing arrives soon", Feature 092 follow-up).
- The PWA DI (`Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`) does **not** register
  `IPersonaClient` / `IPersonaService` (the web app's `Sorcha.UI.Core` does).
- Backend `PUT /api/me/persona` (Tenant `PersonaEndpoints`) is fully functional. So the fix is to
  build the PWA persona-edit surface: register the persona client/service, render an edit form bound
  to `PersonaAttributesV1`, call `IPersonaService.UpdateAsync`, reload after save.
- Medium-large (the punted F092 PWA migration). Needs the consumer-tier token to carry
  `platform_user_id` (see item 3 — likely shared cause).

**b) x-persona mapping — CONFIRMED CORRECT.**
- Assured Identity blueprint (`walkthroughs/AssuredIdentity/blueprints/assured-identity.json`, Action 1)
  uses sorcha-core primitives whose `x-persona` tags align with the persona model
  (`Sorcha.Tenant.Models/Persona/PersonaAttributesV1.cs`):
  `givenName, middleName, familyName, fullName, dateOfBirth` → exact match; `email`→`defaultEmail`
  (inferred); postal address via object-shape inference (`address.line1/line2/town/region/postcode/country`).
- `PersonaAutofillResolver` resolves all of these. **No field-key mismatch.** Autofill will populate
  once a persona exists.
- **Why you can't test autofill yet:** persona is empty because the PWA can't save it. **Workaround to
  test today:** set your persona via the **web** `/app` MyProfile (which IS wired), then run the
  assured-identity form — autofill should populate.

## 2. Bell/inbox drawer overflows phone width
**Status:** root cause HIGH confidence — prodexec-ready.
- The width cap lives in component-scoped CSS `InboxPanel.razor.css`:
  `::deep .mud-drawer { width: min(420px,100vw)!important; max-width:100vw; }`.
- Blazor CSS isolation rewrites it to `[b-xxx] .mud-drawer`, but MudBlazor renders the drawer
  **outside** InboxPanel's DOM subtree, so the scoped selector never matches → the cap never applies →
  the drawer stays 420px and overflows ~360–390px phones. (That's why "we fixed it" didn't take — the
  fix was added in the wrong place.)
- **Fix:** move the width cap to **global** CSS — `Sorcha.Wallet.Pwa/wwwroot/css/app.css` (and the web
  `Sorcha.UI.Web/wwwroot/app.css`) so it applies to the `.mud-drawer` wherever MudBlazor mounts it.
  Add `max-width:100%`/`overflow-wrap` on inner content as belt-and-braces. Files:
  `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Inbox/InboxPanel.razor[.css]`.

## 3. Profile → Security: "We couldn't load your security settings."
**Status:** HYPOTHESIS — confirm live first.
- `Sorcha.UI.Components.User/Components/Security/SecurityHome.razor` → `GET /api/me/auth-methods` (Tenant).
- Likely **404** (gateway doesn't route `/api/me/*` to Tenant from the `/wallet` host) **or 401**
  (consumer-tier token missing `platform_user_id` → `ResolvePlatformUserIdAsync` returns null).

## 4. "My devices": "Could not load devices.."
**Status:** HYPOTHESIS — confirm live first.
- `Sorcha.Wallet.Pwa/Pages/Devices.razor` → `GET /api/v1/wallet/devices` (Wallet service) via
  `ICitizenWalletClient.ListDevicesAsync`. Likely **401** (consumer token audience/claim).

## 5. "Add my phone" → "couldn't generate a pairing code" + page looks unstyled
**Status:** HYPOTHESIS (auth) + HIGH-confidence (styling).
- `Sorcha.Wallet.Pwa/Components/PairingTakeover.razor` + `Sorcha.UI.Components.User/Components/EnrolGate/WalletPairingSurface.razor`
  → `POST /api/auth/enrol-session` (Tenant, `RequireAuthorization`). Likely **401** (missing
  `platform_user_id`).
- "Missing design": the overlay CSS (`sorcha-welcome-overlay` / `welcome-takeover.css`) isn't loading
  in the PWA context → confirm the stylesheet is referenced/bundled in the PWA host.

## Likely shared root cause for 1a / 3 / 5 — CONFIRM FIRST
`/api/me/*` and `/api/auth/enrol-session` are **Tenant** endpoints that resolve `platform_user_id`
from the JWT. If the PWA **consumer-tier** token (F136) omits `platform_user_id`, or the gateway
doesn't route those paths from the `/wallet` host to Tenant, then persona-save, security-settings,
and pairing-code all fail together. **Action: probe the three endpoints' actual status codes on n1
with a real consumer token before fixing** — one fix (token claim or gateway route) may clear several.
Item 4 (Wallet `/api/v1/wallet/devices`) may be a separate 401.

---

## 6. App icon is wrong on mobile (shows the default Capacitor icon)
**Status:** root cause HIGH confidence. **Automatable: YES.**
- The mobile app (`mobile/wallet`) ships the **framework default launcher icons** — iOS
  `mobile/wallet/ios/App/App/Assets.xcassets/AppIcon.appiconset/` has only the placeholder
  `AppIcon-512@2x.png`; Android `mobile/wallet/android/app/src/main/res/mipmap-*/ic_launcher*.png`
  are the default Capacitor/Android icons. There is **no branded source icon** in the repo and
  **no icon-generation tooling** (`@capacitor/assets` / `cordova-res` absent from every `package.json`).
- **Automation (recommended):** add the `@capacitor/assets` dev dependency, drop a single branded
  source `mobile/wallet/assets/icon.png` (1024×1024; plus `icon-foreground.png` + `icon-background.png`
  for Android adaptive icons, and `splash.png` for the splash screen), then `npx @capacitor/assets
  generate` regenerates the entire iOS `AppIcon.appiconset` and all Android mipmap densities
  (incl. adaptive foreground) from that one source. Wire the `generate` step into the Mac build
  pipeline (fastlane / the mobile build workflow) so icons rebuild from source every build.
- Same source asset should also feed the **PWA manifest icons** (`Sorcha.Wallet.Pwa/wwwroot`).
- **Only human input needed:** the branded 1024×1024 source icon (a design deliverable — pairs with
  the deferred visual/wording refresh). Everything downstream is automated.

## Also answered this session
- **"Is camera-first in main?"** — **No.** No PR for `159-pwa-present-camera-first`, no camera-first
  commit on master. It's still inside prodexec run `829d3e0ef4ca` (resumed after the daemon restart,
  mid review round 4). Only the nav drawer (#1040) has landed.
