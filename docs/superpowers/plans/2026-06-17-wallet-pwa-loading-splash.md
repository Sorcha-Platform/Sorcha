# Wallet PWA Loading Splash Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Wallet PWA's static "Loading Sorcha Wallet…" white page with a branded, animated splash that shows honest load feedback (real % during download, indeterminate status text for the startup tail).

**Architecture:** Pure vanilla HTML/CSS/JS inside `<div id="app">` in `index.html`, so it paints *before* Blazor WASM boots. A `<canvas>` runs the landing-page network animation behind a glowing "S". The indicator is driven by Blazor's real `--blazor-load-percentage` CSS custom property; when it reaches 100 the bar fades out and shimmering status text fades in. Blazor renders `App` into `#app` on first render, which removes the splash automatically; a `MutationObserver` cancels the animation loop at that point.

**Tech Stack:** Blazor WebAssembly (.NET 10), vanilla Canvas 2D + CSS, NUnit + Playwright E2E (`Sorcha.UI.E2E.Tests`, Docker-hosted).

**Implementation note (deviation from spec):** The spec listed two signals for the download→startup handoff — `Blazor.start()` resolving (requires `autostart="false"`) and the `--blazor-load-percentage` property. This plan uses **`--blazor-load-percentage` reaching 100** and does **not** touch `autostart`, to avoid any risk of breaking the boot sequence. A defensive fallback (below) covers the case where the property is never readable, so the splash degrades gracefully to the indeterminate-text experience the user already approved.

**Prerequisites for running tests:** The Docker stack must be up (`docker-compose up -d`) and the PWA reachable at `http://localhost:80/wallet/`. The E2E suite is NUnit + Playwright; run individual tests with `dotnet test tests/Sorcha.UI.E2E.Tests --filter "<name>"`.

---

## File Structure

- **Create** `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/splash.css` — all splash styling, incl. reduced-motion. Self-contained, no MudBlazor tokens (paints before MudBlazor CSS is meaningful).
- **Create** `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js` — canvas animation, `--blazor-load-percentage` reader, bar→text handoff, fallback, reduced-motion static frame, and `MutationObserver` cleanup.
- **Modify** `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html` — replace the static loading `<div>` with splash markup; add `<link>` to `css/splash.css`; add `<script>` for `js/splash.js` before `blazor.webassembly.js`.
- **Create** `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs` — markup/asset-wiring + boot-still-works + reduced-motion guards.
- **Modify** `src/Apps/Sorcha.Wallet.Pwa/README.md` (if present) — short note on the splash.
- **Modify** `.gitignore` — ignore `.superpowers/` (brainstorm companion artifacts).

Responsibilities are cleanly split: `index.html` declares structure, `splash.css` owns appearance, `splash.js` owns behaviour + the Blazor handshake.

---

### Task 1: Splash visual shell (CSS + markup)

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/splash.css`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html`
- Test: `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Guards the Wallet PWA pre-boot loading splash: the markup + assets are
/// served in index.html, and the splash is removed once Blazor renders the
/// app into #app (proving the splash wiring did not break boot).
/// </summary>
public class WalletSplashTests : AuthenticatedCitizenWalletTestBase
{
    private static string WalletUrl(string suffix = "") =>
        $"{TestConstants.UiWebUrl}{TestConstants.CitizenWalletBase}{suffix}";

    [Test]
    public async Task IndexHtml_ContainsSplashMarkupAndStylesheet()
    {
        using var http = new HttpClient();
        var html = await http.GetStringAsync(WalletUrl());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("id=\"sorcha-splash\""), "splash root present");
            Assert.That(html, Does.Contain("sorcha-splash-canvas"), "canvas present");
            Assert.That(html, Does.Contain("sorcha-splash-fill"), "progress bar present");
            Assert.That(html, Does.Contain("sorcha-splash-status"), "status text present");
            Assert.That(html, Does.Contain("css/splash.css"), "splash.css linked");
        });
    }

    [Test]
    public async Task SplashStylesheet_IsServed()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(WalletUrl("css/splash.css"));
        Assert.That((int)res.StatusCode, Is.EqualTo(200), "css/splash.css should be served");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests.IndexHtml_ContainsSplashMarkupAndStylesheet"`
Expected: FAIL — `html` does not contain `id="sorcha-splash"` (splash not added yet).

- [ ] **Step 3: Create the splash stylesheet**

Create `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/splash.css`:

```css
/* SPDX-License-Identifier: MIT
   Copyright (c) 2026 Sorcha Contributors */

/* Pre-boot loading splash for the Sorcha Wallet PWA. Rendered inside #app in
   index.html and shown until Blazor WASM finishes booting and replaces #app's
   content with the App component. Pure CSS — no MudBlazor tokens — because this
   paints before the MudBlazor stylesheet/theme are meaningful. Always dark: it
   is a brand surface and the user's theme is not known until the app boots. */

.sorcha-splash {
  position: fixed;
  inset: 0;
  overflow: hidden;
  background: #090A14;
  font-family: 'Inter', system-ui, -apple-system, sans-serif;
  z-index: 9999;
}
.sorcha-splash canvas { position: absolute; inset: 0; width: 100%; height: 100%; }
.sorcha-splash-center {
  position: absolute; inset: 0; z-index: 2;
  display: flex; flex-direction: column; align-items: center; justify-content: center;
}
.sorcha-splash-mark {
  width: 92px; height: 92px; border-radius: 24px;
  background: linear-gradient(135deg, #0d0e1c 0%, #14162b 100%);
  border: 1px solid rgba(99, 102, 241, .35);
  display: flex; align-items: center; justify-content: center;
  box-shadow: 0 0 50px -6px rgba(99, 102, 241, .55),
              inset 0 0 30px -10px rgba(129, 140, 248, .4);
}
.sorcha-splash-mark span {
  font-family: 'Helvetica Neue', Arial, sans-serif; font-weight: 900;
  font-size: 60px; line-height: 1; color: #fff;
  text-shadow: 0 0 22px rgba(129, 140, 248, .9), 0 0 40px rgba(99, 102, 241, .6);
  animation: sorcha-splash-breathe 2.8s ease-in-out infinite;
}
@keyframes sorcha-splash-breathe {
  0%, 100% { opacity: .85; transform: scale(1); }
  50%      { opacity: 1;   transform: scale(1.04); }
}
.sorcha-splash-wordmark {
  margin-top: 20px; font-size: 1.05rem; font-weight: 600;
  letter-spacing: .02em; color: #f2f3fb;
}
.sorcha-splash-subword {
  margin-top: 4px; font-size: .72rem; letter-spacing: .22em;
  text-transform: uppercase; color: rgba(129, 140, 248, .75);
}
.sorcha-splash-ind {
  position: absolute; left: 0; right: 0; bottom: 14%; z-index: 2;
  display: flex; justify-content: center;
}
/* progress bar (phase 1) */
.sorcha-splash-bar { width: 180px; transition: opacity .4s ease; }
.sorcha-splash-track {
  height: 4px; border-radius: 4px; background: rgba(255, 255, 255, .08); overflow: hidden;
}
.sorcha-splash-fill {
  height: 100%; width: 0%; border-radius: 4px;
  background: linear-gradient(90deg, #4F46E5, #818CF8);
  box-shadow: 0 0 12px rgba(129, 140, 248, .7);
  transition: width .2s ease;
}
.sorcha-splash-pct {
  margin-top: 8px; text-align: center; font-size: .72rem; letter-spacing: .04em;
  color: #9aa0c8; font-variant-numeric: tabular-nums;
}
/* status text (phase 2 / fallback) — hidden until handoff */
.sorcha-splash-text {
  position: absolute; left: 0; right: 0; text-align: center;
  opacity: 0; transition: opacity .5s ease;
}
.sorcha-splash-status {
  font-size: .8rem; letter-spacing: .03em;
  background: linear-gradient(90deg, #6b72a8 25%, #fff 50%, #6b72a8 75%);
  background-size: 200% auto;
  -webkit-background-clip: text; background-clip: text; -webkit-text-fill-color: transparent;
  animation: sorcha-splash-shimmer 2.4s linear infinite;
}
@keyframes sorcha-splash-shimmer { to { background-position: -200% center; } }
.sorcha-splash-dots {
  display: inline-flex; gap: 5px; margin-top: 14px; width: 100%; justify-content: center;
}
.sorcha-splash-dots i {
  width: 6px; height: 6px; border-radius: 50%;
  background: rgba(129, 140, 248, .5);
  animation: sorcha-splash-pulse 1.4s ease-in-out infinite;
}
.sorcha-splash-dots i:nth-child(2) { animation-delay: .2s; }
.sorcha-splash-dots i:nth-child(3) { animation-delay: .4s; }
@keyframes sorcha-splash-pulse {
  0%, 100% { opacity: .3; transform: scale(.8); }
  50%      { opacity: 1;  transform: scale(1.15); }
}
/* State class toggled by splash.js at the download→startup handoff. */
.sorcha-splash.is-startup .sorcha-splash-bar  { opacity: 0; }
.sorcha-splash.is-startup .sorcha-splash-text { opacity: 1; }

@media (prefers-reduced-motion: reduce) {
  .sorcha-splash-mark span,
  .sorcha-splash-status,
  .sorcha-splash-dots i { animation: none !important; }
  .sorcha-splash-status { -webkit-text-fill-color: #aab0d8; color: #aab0d8; }
}
```

- [ ] **Step 4: Add the stylesheet link and splash markup to index.html**

In `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html`, add the stylesheet link in `<head>` immediately after the existing `css/welcome-takeover.css` line:

```html
    <link href="css/splash.css" rel="stylesheet" />
```

Then replace the entire existing `<div id="app">…</div>` block:

```html
    <div id="app">
        <div style="font-family: system-ui, sans-serif; padding: 2rem; text-align: center;">
            Loading Sorcha Wallet…
        </div>
    </div>
```

with:

```html
    <div id="app">
        <div id="sorcha-splash" class="sorcha-splash" role="status" aria-live="polite"
             aria-label="Loading Sorcha Wallet">
            <canvas id="sorcha-splash-canvas" aria-hidden="true"></canvas>
            <div class="sorcha-splash-center">
                <div class="sorcha-splash-mark"><span>S</span></div>
                <div class="sorcha-splash-wordmark">Sorcha Wallet</div>
                <div class="sorcha-splash-subword">Your proof, your device</div>
            </div>
            <div class="sorcha-splash-ind">
                <div class="sorcha-splash-bar" id="sorcha-splash-bar">
                    <div class="sorcha-splash-track">
                        <div class="sorcha-splash-fill" id="sorcha-splash-fill"></div>
                    </div>
                    <div class="sorcha-splash-pct" id="sorcha-splash-pct">0%</div>
                </div>
                <div class="sorcha-splash-text" id="sorcha-splash-text">
                    <div class="sorcha-splash-status" id="sorcha-splash-status">Starting up…</div>
                    <div class="sorcha-splash-dots"><i></i><i></i><i></i></div>
                </div>
            </div>
        </div>
    </div>
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests.IndexHtml_ContainsSplashMarkupAndStylesheet|WalletSplashTests.SplashStylesheet_IsServed"`
Expected: PASS (both tests). If `SplashStylesheet_IsServed` fails with 404, the Docker image predates the new file — rebuild the PWA container: `docker-compose build sorcha-ui && docker-compose up -d --force-recreate sorcha-ui` (the PWA is served by the UI host).

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/splash.css \
        src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html \
        tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs
git commit -m "feat(pwa): branded loading splash markup + styles"
```

---

### Task 2: Splash behaviour (canvas animation + honest progress)

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html`
- Test: `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these two tests to the existing `WalletSplashTests` class (inside the class body, after the Task 1 tests):

```csharp
    [Test]
    public async Task SplashScript_IsServed()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(WalletUrl("js/splash.js"));
        Assert.That((int)res.StatusCode, Is.EqualTo(200), "js/splash.js should be served");
    }

    [Test]
    public async Task Splash_RemovedAfterBlazorHydration()
    {
        // Navigating + waiting for Blazor proves two things at once: the splash
        // markup/script did not break boot, and Blazor's render into #app
        // removes the splash (it lives inside #app, which Blazor clears).
        await NavigateToWalletAndWaitForBlazorAsync();

        var count = await Page.Locator("#sorcha-splash").CountAsync();
        Assert.That(count, Is.Zero,
            "Splash should be gone once Blazor renders App into #app.");
    }
```

Also add to `IndexHtml_ContainsSplashMarkupAndStylesheet`'s `Assert.Multiple` block a check that the script is referenced:

```csharp
            Assert.That(html, Does.Contain("js/splash.js"), "splash.js referenced");
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests.SplashScript_IsServed"`
Expected: FAIL with 404 — `js/splash.js` does not exist yet.

- [ ] **Step 3: Create the splash behaviour script**

Create `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js`:

```javascript
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Pre-boot loading splash for the Sorcha Wallet PWA.
//
// Runs before blazor.webassembly.js finishes booting. Draws a lightweight
// network animation (the landing-page hero aesthetic) behind the glowing "S",
// and shows HONEST load feedback:
//
//   Phase 1 (download): a progress bar driven by Blazor's real
//     `--blazor-load-percentage` CSS custom property (set by the runtime as
//     boot resources download). Climbs 0 -> 100 truthfully.
//   Handoff: the instant the percentage reaches 100 (download complete), the
//     bar fades out and indeterminate status text fades in — so the number is
//     never seen stalling at 100% during the unmeasurable init/render tail.
//   Phase 2 (startup tail): shimmering "Starting up… / Almost ready…" until
//     Blazor renders App into #app, which removes this splash.
//
// Fallback: if the percentage is never readable (older runtimes / fully cached
// boot), after FALLBACK_MS we drop the bar and show an indeterminate
// "Loading your wallet…" — never a fake number. The exact element the runtime
// sets the property on varies, so readPct() checks both #app and the document
// root.
//
// Cleanup: a MutationObserver cancels the animation loop and timers the moment
// #sorcha-splash leaves the DOM, so a detached canvas never keeps a rAF loop
// running after the app has taken over.

(function () {
  "use strict";

  var root = document.getElementById("sorcha-splash");
  if (!root) return;

  var canvas = document.getElementById("sorcha-splash-canvas");
  var fill = document.getElementById("sorcha-splash-fill");
  var pct = document.getElementById("sorcha-splash-pct");
  var statusEl = document.getElementById("sorcha-splash-status");

  var reduce = window.matchMedia &&
    window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  var raf = null;
  var tailTimer = null;
  var fallbackTimer = null;
  var handedOff = false;

  // ---- network animation ----
  var ctx = canvas.getContext("2d");
  var w = 0, h = 0, dpr = 1, peers = [], t = 0;
  var C = { r: 99, g: 102, b: 241 };
  var GRID = 38, PEERS = 14, LINK = 95;

  function size() {
    dpr = window.devicePixelRatio || 1;
    var r = canvas.getBoundingClientRect();
    w = r.width; h = r.height;
    canvas.width = w * dpr; canvas.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }
  function initPeers() {
    peers = [];
    for (var i = 0; i < PEERS; i++) {
      peers.push({
        x: Math.random() * w, y: Math.random() * h,
        vx: (Math.random() - 0.5) * 0.12, vy: (Math.random() - 0.5) * 0.12,
        sz: 3 + Math.random() * 3, ph: Math.random() * 6.28
      });
    }
  }
  function draw(animate) {
    t += 0.016;
    ctx.clearRect(0, 0, w, h);
    var g = ctx.createLinearGradient(0, 0, w, h);
    g.addColorStop(0, "#090A14"); g.addColorStop(1, "#0F1020");
    ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
    var bl = ctx.createRadialGradient(w / 2, h * 0.42, 10, w / 2, h * 0.42, h * 0.42);
    bl.addColorStop(0, "rgba(" + C.r + "," + C.g + "," + C.b + ",0.12)");
    bl.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = bl; ctx.fillRect(0, 0, w, h);
    ctx.strokeStyle = "rgba(" + C.r + "," + C.g + "," + C.b + ",0.06)";
    ctx.lineWidth = 1;
    for (var x = 0; x < w; x += GRID) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke(); }
    for (var y = 0; y < h; y += GRID) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke(); }
    for (var i = 0; i < peers.length; i++) {
      for (var j = i + 1; j < peers.length; j++) {
        var a = peers[i], b = peers[j];
        var d = Math.hypot(a.x - b.x, a.y - b.y);
        if (d < LINK) {
          ctx.strokeStyle = "rgba(" + C.r + "," + C.g + "," + C.b + "," + (0.10 * (1 - d / LINK)) + ")";
          ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
        }
      }
    }
    for (var k = 0; k < peers.length; k++) {
      var p = peers[k];
      if (animate) {
        p.x += p.vx; p.y += p.vy;
        if (p.x < 0 || p.x > w) p.vx *= -1;
        if (p.y < 0 || p.y > h) p.vy *= -1;
      }
      var tw = 0.5 + 0.5 * Math.sin(t * 1.5 + p.ph);
      var gl = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, 12);
      gl.addColorStop(0, "rgba(" + C.r + "," + C.g + "," + C.b + "," + (0.5 * tw) + ")");
      gl.addColorStop(1, "rgba(0,0,0,0)");
      ctx.fillStyle = gl; ctx.fillRect(p.x - 12, p.y - 12, 24, 24);
      ctx.fillStyle = "rgba(129,140,248," + (0.6 + 0.4 * tw) + ")";
      ctx.fillRect(p.x - p.sz / 2, p.y - p.sz / 2, p.sz, p.sz);
    }
  }
  function animLoop() {
    draw(true);
    raf = window.requestAnimationFrame(animLoop);
  }

  // ---- progress / handoff ----
  function readPct() {
    var els = [document.getElementById("app"), document.documentElement];
    for (var i = 0; i < els.length; i++) {
      if (!els[i]) continue;
      var raw = getComputedStyle(els[i]).getPropertyValue("--blazor-load-percentage").trim();
      var n = parseFloat(raw);
      if (!isNaN(n)) return Math.max(0, Math.min(100, n));
    }
    return null;
  }
  function handoff(message) {
    if (handedOff) return;
    handedOff = true;
    if (fallbackTimer) { clearTimeout(fallbackTimer); fallbackTimer = null; }
    root.classList.add("is-startup");
    if (message != null) {
      if (statusEl) statusEl.textContent = message;
      return;
    }
    // Real completion: cycle calm tail copy (unless reduced motion).
    if (reduce) return;
    var msgs = ["Starting up…", "Almost ready…"];
    var i = 0;
    if (statusEl) statusEl.textContent = msgs[0];
    tailTimer = window.setInterval(function () {
      i = (i + 1) % msgs.length;
      if (statusEl) statusEl.textContent = msgs[i];
    }, 1600);
  }
  function progressLoop() {
    if (handedOff) return;
    var p = readPct();
    if (p != null) {
      if (fill) fill.style.width = p + "%";
      if (pct) pct.textContent = Math.round(p) + "%";
      if (p >= 100) { handoff(null); return; }
    }
    window.requestAnimationFrame(progressLoop);
  }

  // ---- cleanup when Blazor replaces #app ----
  function teardown() {
    if (raf) { window.cancelAnimationFrame(raf); raf = null; }
    if (tailTimer) { clearInterval(tailTimer); tailTimer = null; }
    if (fallbackTimer) { clearTimeout(fallbackTimer); fallbackTimer = null; }
  }
  var appHost = document.getElementById("app");
  if (appHost && window.MutationObserver) {
    var obs = new MutationObserver(function () {
      if (!document.getElementById("sorcha-splash")) {
        teardown();
        obs.disconnect();
      }
    });
    obs.observe(appHost, { childList: true, subtree: true });
  }

  // ---- start ----
  size();
  initPeers();
  if (reduce) {
    draw(false); // single static frame, no loop
  } else {
    animLoop();
  }
  window.addEventListener("resize", function () { size(); initPeers(); });

  // Fallback: no readable percentage within FALLBACK_MS -> indeterminate text.
  var FALLBACK_MS = 1500;
  fallbackTimer = window.setTimeout(function () {
    if (!handedOff && readPct() == null) handoff("Loading your wallet…");
  }, FALLBACK_MS);

  window.requestAnimationFrame(progressLoop);
})();
```

- [ ] **Step 4: Wire the script into index.html**

In `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html`, add the script tag immediately **before** the `blazor.webassembly.js` line (so the splash starts animating as early as possible):

```html
    <script src="js/splash.js"></script>
    <script src="_framework/blazor.webassembly.js"></script>
```

- [ ] **Step 5: Run tests to verify they pass**

Rebuild the PWA container so the new static files are served, then run:

```bash
docker-compose build sorcha-ui && docker-compose up -d --force-recreate sorcha-ui
dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests"
```

Expected: PASS for all four tests (`IndexHtml_ContainsSplashMarkupAndStylesheet`, `SplashStylesheet_IsServed`, `SplashScript_IsServed`, `Splash_RemovedAfterBlazorHydration`).

- [ ] **Step 6: Manual visual check**

Open `http://localhost:80/wallet/` in a browser with a cold cache (DevTools → Network → Disable cache, then hard reload). Confirm: dark splash with drifting glowing squares + breathing "S", a progress bar that advances, then a fade to "Starting up…/Almost ready…", then the wallet home renders and the splash disappears. (On a fast/cached load the bar may flash past straight to text — expected.)

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js \
        src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html \
        tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs
git commit -m "feat(pwa): honest progress-then-text splash behaviour"
```

---

### Task 3: Reduced-motion guard, docs, and housekeeping

**Files:**
- Test: `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/README.md` (if present)
- Modify: `.gitignore`

- [ ] **Step 1: Write the failing reduced-motion test**

Add to the `WalletSplashTests` class:

```csharp
    [Test]
    public async Task Splash_ReducedMotion_StillBootsAndRemovesSplash()
    {
        // With reduced motion, splash.js draws a single static canvas frame and
        // runs no rAF loop. Boot must still complete and the splash must still
        // be removed — i.e. the reduced-motion branch doesn't wedge startup.
        await Page.EmulateMediaAsync(new() { ReducedMotion = Microsoft.Playwright.ReducedMotion.Reduce });
        await NavigateToWalletAndWaitForBlazorAsync();

        var count = await Page.Locator("#sorcha-splash").CountAsync();
        Assert.That(count, Is.Zero,
            "Splash should be removed after hydration even under reduced motion.");
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests.Splash_ReducedMotion_StillBootsAndRemovesSplash"`
Expected: PASS. (The reduced-motion CSS/JS branches were already implemented in Tasks 1–2; this test confirms they don't break boot. If it fails because the splash never clears, check the `MutationObserver` teardown path in `splash.js`.)

- [ ] **Step 3: Add a README note (only if the PWA README exists)**

Check for `src/Apps/Sorcha.Wallet.Pwa/README.md`. If it exists, add this section near the top-level feature list:

```markdown
## Loading splash

The pre-boot loading screen lives in `wwwroot/index.html` (`#sorcha-splash`),
styled by `wwwroot/css/splash.css` and driven by `wwwroot/js/splash.js`. It runs
before Blazor boots: a canvas network animation behind the glowing "S", a real
progress bar fed by `--blazor-load-percentage` during download, then indeterminate
status text for the init/startup tail. Blazor removes it by rendering `App` into
`#app`. Respects `prefers-reduced-motion`.
```

If there is no README, skip this step (do not create one — out of scope).

- [ ] **Step 4: Ignore the brainstorm companion artifacts**

Confirm `.gitignore` does not already cover it (`grep -n "superpowers" .gitignore`). If absent, append:

```
# Superpowers brainstorm visual-companion artifacts
.superpowers/
```

- [ ] **Step 5: Commit**

```bash
git add tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs .gitignore
git add src/Apps/Sorcha.Wallet.Pwa/README.md 2>/dev/null || true
git commit -m "test(pwa): reduced-motion splash guard; docs + gitignore"
```

---

## Self-Review

**Spec coverage:**
- Branded canvas network animation + glowing "S" → Task 1 (markup/CSS) + Task 2 (`splash.js` `draw`).
- Pre-boot, zero-framework constraint → splash is vanilla HTML/CSS/JS inside `#app`; no Blazor APIs touched.
- Honest hybrid indicator (real % → text handoff) → Task 2 `progressLoop`/`handoff` driven by `--blazor-load-percentage`; `.is-startup` CSS does the fade.
- No fake number / no stall at 100% → handoff fires *at* 100 and removes the bar; fallback shows indeterminate text, never a number.
- Self-removal + animation cleanup → Blazor clears `#app`; `MutationObserver` teardown cancels rAF/timers (Task 2).
- Reduced motion → CSS media query (Task 1) + static-frame branch (Task 2) + guard test (Task 3).
- Always-dark brand surface → `splash.css` hardcodes dark palette; documented in file header.
- Testing (markup present, served, removed after hydration, reduced motion, throttled/manual) → Tasks 1–3 tests + Step 2.6 manual check.
- Copy defaults ("Your proof, your device", "Starting up…/Almost ready…", fallback "Loading your wallet…") → in markup + `splash.js`.

**Placeholder scan:** No TBD/TODO; all code blocks are complete and copy-paste ready.

**Type/name consistency:** Element IDs (`sorcha-splash`, `sorcha-splash-canvas`, `sorcha-splash-fill`, `sorcha-splash-pct`, `sorcha-splash-status`, `app`), the `is-startup` class, and the `--blazor-load-percentage` property name match across `index.html`, `splash.css`, `splash.js`, and the tests. Test method/class names (`WalletSplashTests`, `WalletUrl`) are consistent across tasks. `NavigateToWalletAndWaitForBlazorAsync`/`NavigateToWalletAsync` exist on `AuthenticatedCitizenWalletTestBase`.
