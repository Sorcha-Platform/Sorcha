# PWA/Web UI fixes — Navigation drawer + Present camera-first (PR A)

**Date:** 2026-06-25
**Status:** Approved design — ready for implementation
**Scope:** Two independent, small UI changes shipped together in one PR. Part of a larger
"PWA & apps tidy-up" initiative whose remaining workstreams (Verify unification, visual/wording
refresh) are tracked separately and explicitly OUT OF SCOPE here.

---

## Background

A walkthrough of the web app and Citizen Wallet PWA surfaced two concrete, low-risk UX defects
that can be fixed independently of the larger Verify refactor:

1. The web app's left navigation uses MudBlazor's **Mini** drawer variant uniformly across all
   screen sizes. When "closed" it collapses to a ~56px icon rail that **stays in the layout and
   keeps a content offset**. On a phone that rail consumes ~15% of width and is dead space (no
   hover-to-expand on touch). The user's report — "compressed menu that takes too much space and
   doesn't work right" — is exactly this Mini behaviour.

2. The PWA **Present** flow (holder presents a credential to a verifier) defaults to a
   **paste-a-deeplink textbox** as the primary surface, with QR camera scanning as a secondary
   button — even though a fully working camera scanner already exists. On a phone the natural
   action is to point the camera at the verifier's QR, not paste a link.

Both are reprioritisation/configuration changes, not new subsystems. No backend work.

---

## Item 1 — Navigation drawer → Responsive

### Current state
`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`:

```razor
<MudDrawer @bind-Open="_drawerOpen" Variant="@DrawerVariant.Mini"
           OpenMiniOnHover="true" ClipMode="DrawerClipMode.Always" Elevation="2">
```

`_drawerOpen` initialises `true`; there is **no responsive breakpoint logic**; the Mini variant
applies identically on a 375px phone and a 1920px desktop. Section headers/dividers render only
`@if (_drawerOpen)` (a Mini-mode accommodation). Theme `DrawerWidthLeft = "260px"`.

### Target behaviour
Switch to MudBlazor's **Responsive** drawer variant so the drawer **fully releases its space when
closed** — no mini rail in any state.

- `Variant="DrawerVariant.Responsive"`, remove `OpenMiniOnHover`, keep `ClipMode="DrawerClipMode.Always"`
  (drawer sits below the app bar).
- Breakpoint = MudBlazor default `Breakpoint.Md` (960px):
  - **≥ Md (desktop/wide):** persistent, open by default, content pushes right; hamburger fully
    hides it and content reflows to full width (no rail).
  - **< Md (phone/narrow):** temporary overlay, **closed by default**; hamburger opens it as an
    overlay that dims the content beneath; closing returns content to full width.
- Initialise `_drawerOpen` by viewport so a phone does not load with the drawer covering content:
  resolve the current breakpoint via `IBrowserViewportService` (already available in the app) on
  first render and set `_drawerOpen = breakpoint >= Breakpoint.Md`. `ToggleDrawer()` is unchanged.
- Remove the `@if (_drawerOpen)` guards around section headers/dividers — with no Mini state the
  headers simply render whenever the drawer is shown.

### Acceptance
- Desktop: drawer open by default, pushes content; toggling closed leaves **no** left rail and
  content occupies full width.
- Phone (≤ ~600px): drawer closed by default, content full width; hamburger opens an overlay;
  closing restores full width. No persistent rail at any point.
- No console errors; MudBlazor CSS health intact.

### Tests
Extend the existing Docker E2E suite (`tests/Sorcha.UI.E2E.Tests`):
- `NavigationTests`: closing the drawer on a desktop viewport releases the content offset (assert
  the main content's left offset returns to ~0 / no ~56px rail element remains interactive).
- `DashboardTests` responsive cases (375×667 phone, 1920×1080 desktop): assert phone loads with
  drawer closed + full-width content; desktop loads with drawer open. Add `data-testid`s on the
  drawer/content regions where needed for stable assertions.

---

## Item 2 — Present camera-first

### Current state
`src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` already integrates a working QR scanner via the
`SorchaQrScanner` JS bridge (`wwwroot/js/qr-scanner-bridge.js`, `nimiq/qr-scanner`, camera via
`getUserMedia`, with NotAllowed/NotFound/cancel handling). Today the **paste textbox is the
always-visible primary** surface and "Scan QR code" is a secondary button. Downstream parsing is
`PresentationEngine.Parse` of the scanned/pasted `openid4vp://` URL → consent → vp_token POST.

### Target behaviour
Make the camera the default capture surface **on devices that warrant it**, with paste always
reachable. Decide via two client probes on load (no new JS — reuse the existing bridge):

- `hasCamera` — `SorchaQrScanner.isSupported()` plus a camera-device check.
- `isHandheld` — `window.matchMedia('(pointer: coarse)')` (touch-primary device).

Surface selection:
- **Handheld + camera** → camera scanner **auto-opens** as the default surface; a
  **"Paste a link instead"** button reveals the textbox.
- **Desktop/laptop (any pointer:fine)** → paste textbox is the default surface, **plus** a
  prominent **"Scan with camera"** button (most laptops have a webcam, but we do not ambush the
  user with an auto webcam-permission prompt).
- **No camera** → paste only; no scan affordance shown.

Downstream parse/consent/POST flow is unchanged. Camera permission denial falls back gracefully to
the paste surface (existing error handling already distinguishes NotAllowed/NotFound).

### Acceptance
- On a touch device with a camera the scanner is the first thing shown; paste is one tap away.
- On a desktop the paste box is shown with a clearly visible "Scan with camera" button.
- On a device with no camera, only paste is shown.
- The paste path still parses an `openid4vp://` link exactly as today.

### Tests
Present flow currently has **no** E2E coverage — add `PresentTests` (Playwright, Chromium launched
with `--use-fake-device-for-media-stream` to provide a fake camera):
- Mobile viewport + fake camera → scanner is the default surface and "Paste a link instead" is
  present.
- Desktop viewport → paste is the default surface and a "Scan with camera" button is present.
- Paste path: entering a valid `openid4vp://` link parses without console errors.

---

## Out of scope (tracked separately)
- **Verify unification** onto a single HAIP `request_uri`/`direct-post` transport with a shared
  verify control (lifting `Sorcha.Verifier`'s preset-question → QR → 4-layer verdict-trail
  experience into `Sorcha.UI.Components.User`, retiring the PWA paste `VerifyFlow` and the desk
  verifier's bespoke pages). This is PR B and gets its own design session.
- **PWA visual refresh + copy/wording** updates (pending external design input) — deferred to the
  post-refactor tidy-up pass.

## Risks / notes
- The two items touch different hosts (web `Sorcha.UI.Web.Client` vs `Sorcha.Wallet.Pwa`) and do
  not interact — safe to land together.
- PWA navigation must stay base-relative (path-prefix `/wallet/`); no nav-path changes here.
- No `ISnackbar` usage introduced (CI ratchet); not expected for either item.
- Follow the existing `data-testid` selector discipline for all new E2E assertions.
