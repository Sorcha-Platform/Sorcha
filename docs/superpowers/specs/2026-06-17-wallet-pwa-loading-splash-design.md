# Sorcha Wallet PWA — animated loading splash

**Date:** 2026-06-17
**Status:** Design approved, pending spec review
**Area:** `src/Apps/Sorcha.Wallet.Pwa`

## Problem

The Wallet PWA shows a plain white page with the text "Loading Sorcha Wallet…" for the
few seconds it takes Blazor WebAssembly to download its runtime + framework/app DLLs and
boot. It is static, off-brand, and gives the user no signal that anything is happening —
on a slow connection it reads as a broken/blank page.

We want a branded, dynamic loading splash that:

- Reuses the existing Sorcha visual identity (the landing-page hero network animation +
  the PWA icon's glowing "S" on dark purple-black).
- Gives **honest** progress feedback — no fake percentage that stalls at 100%.
- Works **before Blazor has booted**, so it must be pure vanilla HTML/CSS/JS with zero
  framework dependency.

## Constraints (why the obvious approaches don't work)

The splash lives inside `<div id="app">` in
[index.html](../../../src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html), which is the markup
shown *before* `blazor.webassembly.js` has loaded the runtime. Therefore:

- **No Blazor, no MudBlazor, no C#.** The splash is vanilla JS + CSS only.
- **It self-removes for free.** When Blazor renders the root `App` component it replaces
  the inner content of `#app`, so the splash markup disappears automatically on first
  render. We only need to ensure the animation loop is cancelled so a detached canvas
  doesn't keep burning a `requestAnimationFrame` loop.

### The percentage honesty problem

Blazor exposes a **real** download metric via the `--blazor-load-percentage` CSS custom
property (set automatically by the runtime as boot resources download). But it reaches
100% **before the app is interactive** — after download there is an unmeasurable tail:
runtime init, first render, and the wallet's own startup (opening IndexedDB via
`indexeddb-bridge.js`, session/device-pairing checks). A naive progress bar therefore
races to 100% and then *sits frozen* during that tail — the worst kind of feedback.

## Design: hybrid "real % during download, text for the tail"

A single full-screen splash with three layers:

1. **Background** — a `<canvas>` running a distilled version of the landing page's
   `heroCanvas` network animation: drifting glowing peer-squares connected by faint lines,
   over a faint violet grid, on the icon's dark gradient (`#090A14 → #0F1020`) with a
   radial violet bloom behind centre. Pure 2D canvas, ~14 particles, cheap.
2. **Centrepiece** — the glowing "S" mark (rounded-square tile, violet glow, gently
   "breathing" via CSS), the "Sorcha Wallet" wordmark, and a small uppercase tagline
   ("Your proof, your device" — placeholder, easily changed/dropped).
3. **Indicator** — phase-driven:
   - **Phase 1 · download (measurable):** a thin gradient progress bar + `%`, driven by
     the **real** `--blazor-load-percentage`. Climbs 0→100 truthfully.
   - **Handoff:** the instant download completes, fade the bar out.
   - **Phase 2 · startup tail (unmeasurable):** fade in shimmering status text that gently
     cycles "Starting up…" → "Almost ready…" with three pulsing dots, until Blazor
     renders the first screen and the splash is replaced.

The percentage is therefore only ever visible **while it is genuinely moving**; it is
removed the moment it would otherwise stall. On a fast/cached load the bar just flickers
past into the text; on a slow connection it is real, moving reassurance.

## Detecting the download-complete handoff

Two real signals are available; the implementation should use the most reliable:

- **Authoritative:** switch `blazor.webassembly.js` to `autostart="false"` and call
  `Blazor.start()` manually. Its returned promise **resolves when the runtime has
  downloaded and started** — that resolution is the precise download→init boundary and is
  the trigger to fade bar→text.
- **Progress fill:** drive the bar width/number by polling
  `getComputedStyle(document.documentElement).getPropertyValue('--blazor-load-percentage')`
  on `requestAnimationFrame` during phase 1. (Alternatively, a `loadBootResource` callback
  passed to `Blazor.start()` can count resources for a smoother fill.)

The first render after `Blazor.start()` resolves has **no** progress signal — that is
exactly the gap the indeterminate text covers.

## Components / files

- **`wwwroot/index.html`** — replace the static loading `<div>` with the splash markup
  (canvas + centre + indicator); add a `<link>` to `css/splash.css` and a `<script>` for
  `js/splash.js`; set the Blazor script to `autostart="false"` and start it from
  `splash.js`.
- **`wwwroot/css/splash.css`** (new) — all splash styling (dark gradient, mark glow,
  breathing/shimmer/pulse keyframes, bar + text indicator). Self-contained, no MudBlazor
  tokens (it renders before MudBlazor CSS is meaningful).
- **`wwwroot/js/splash.js`** (new) — the canvas animation, the `--blazor-load-percentage`
  reader, the `Blazor.start()` call + `.then()` handoff, the bar→text transition, the tail
  message cycle, and animation-loop **cleanup** (cancel `rAF`) once Blazor has started /
  the splash node is removed.

Each unit has one clear job: `index.html` declares structure, `splash.css` owns
appearance, `splash.js` owns behaviour + the Blazor boot handshake.

## Behaviour details

- **Reduced motion:** respect `prefers-reduced-motion: reduce` — render a single static
  canvas frame (no `rAF` loop), keep the bar/text but drop the breathing/shimmer/pulse
  animations. (The landing-page hero already follows this pattern.)
- **Always dark:** the splash is a brand surface, intentionally dark regardless of the
  user's light/dark theme preference (matches the app icon). This also avoids a
  theme-flash since the theme isn't known until the app boots.
- **Error path:** the existing `#blazor-error-ui` block is unchanged and still handles a
  failed boot.
- **Cleanup:** cancel the `requestAnimationFrame` loop when `Blazor.start()` resolves (or
  via a `MutationObserver`/`afterStarted` hook), so the detached canvas doesn't keep
  running after `#app` is replaced.

## Copy (defaults — adjustable)

- **Wordmark:** "Sorcha Wallet"
- **Tagline:** "Your proof, your device"
- **Phase 1:** real `%`
- **Phase 2 tail (cycling):** "Starting up…" → "Almost ready…"

## Testing

- **Playwright (PWA, Docker infra):** assert the splash markup is present on initial load
  (canvas + indicator), that the progress element reflects `--blazor-load-percentage`, and
  that the splash is gone once the app's first authenticated/landing screen renders.
- **Throttled load:** verify under network throttling that the bar advances and the text
  phase is reached (no permanent 100% stall).
- **Reduced motion:** verify no `rAF` loop runs and animations are suppressed when
  `prefers-reduced-motion: reduce` is emulated.
- **Manual:** confirm against the Docker-hosted `/wallet/` route on a cold (uncached) load.

## Out of scope (YAGNI)

- No theming/light-mode variant of the splash.
- No real per-resource byte progress UI beyond what `--blazor-load-percentage` /
  `loadBootResource` already gives.
- No changes to the web (`/app`) host's loading screen — this is PWA-only. (A later
  follow-up could share `splash.js`/`splash.css` if desired.)
