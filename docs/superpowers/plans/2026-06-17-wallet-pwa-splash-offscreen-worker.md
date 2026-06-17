# Wallet PWA Splash — OffscreenCanvas Worker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the Wallet PWA loading-splash canvas animation (drifting "blobs") smooth *through* the WASM-init main-thread freeze by rendering it on a Web Worker via `OffscreenCanvas`, with a graceful main-thread fallback.

**Architecture:** Extract the particle draw code into a DOM-free shared renderer (`splash-render.js`). A worker (`splash-worker.js`) `importScripts` it and drives the rAF loop against a transferred `OffscreenCanvas` on its own thread (immune to main-thread blocking). `splash.js` feature-detects support: if present it spawns the worker and transfers the canvas; otherwise (older Safari, CSP block, reduced motion) it runs the same renderer on the main thread exactly as today. The progress bar, `%`, `--blazor-load-percentage` reads, status-text handoff, fallback timer, and MutationObserver teardown all stay on the main thread unchanged — only the canvas rendering moves.

**Tech Stack:** Vanilla JS (ES5-style to match existing splash files), `OffscreenCanvas`, dedicated `Worker`, Canvas2D. Served as static files by the `sorcha-wallet-pwa` nginx image.

**Context — current state (post-merge of #1011):**
- `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js` currently contains the inline main-thread animation (`size`/`initPeers`/`draw`/`animLoop`) plus the progress/handoff/teardown logic.
- `index.html` loads `<script src="js/splash.js"></script>` immediately before `_framework/blazor.webassembly.js`, and `<link href="css/splash.css">` in `<head>`.
- Root component is `App` → `#app`; Blazor clears `#app` on first render, removing `#sorcha-splash`; a MutationObserver tears down timers/rAF.
- Tests: `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs` (5 NUnit tests). E2E run needs the Docker stack up; Playwright Chromium is installed locally.

**Accepted limitation (documented, not a defect):** if the worker *script* fails to load at runtime *after* the canvas has been transferred (e.g. a 404 — not the CSP case, which throws synchronously and is caught), the background degrades to a static dark gradient rather than animating. The asset is same-origin and asserted served by a test, so this is not expected in practice. The CSP-blocked case throws synchronously from `new Worker(...)` and falls back cleanly to main-thread rendering.

---

## File Structure

- **Create** `wwwroot/js/splash-render.js` — `createSplashRenderer(ctx)` factory: DOM-free particle sim + `frame(animate)` + `resize(w,h,dpr)`. Shared by worker and fallback.
- **Create** `wwwroot/js/splash-worker.js` — dedicated worker; `importScripts("splash-render.js")`; owns the rAF loop against the transferred `OffscreenCanvas`.
- **Modify** `wwwroot/index.html` — add `<script src="js/splash-render.js"></script>` immediately before `js/splash.js` (so the fallback path has the factory).
- **Modify** `wwwroot/js/splash.js` — replace the inline animation block with worker-or-fallback orchestration; terminate the worker in `teardown()`.
- **Modify** `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs` — assert the two new scripts are served and referenced.

---

### Task 1: Shared renderer + worker (inert building blocks)

These two new files change no behaviour yet — `splash.js` doesn't use them until Task 2 — so this task is independently verifiable (files served, render script referenced).

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash-render.js`
- Create: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash-worker.js`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html`
- Test: `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to the `WalletSplashTests` class body:

```csharp
    [Test]
    public async Task SplashRenderScript_IsServed()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(WalletUrl("js/splash-render.js"));
        Assert.That((int)res.StatusCode, Is.EqualTo(200), "js/splash-render.js should be served");
    }

    [Test]
    public async Task SplashWorkerScript_IsServed()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(WalletUrl("js/splash-worker.js"));
        Assert.That((int)res.StatusCode, Is.EqualTo(200), "js/splash-worker.js should be served");
    }
```

And add one assertion inside the existing `IndexHtml_ContainsSplashMarkupAndStylesheet` `Assert.Multiple` block:

```csharp
            Assert.That(html, Does.Contain("js/splash-render.js"), "splash-render.js referenced");
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests.SplashRenderScript_IsServed" --no-build` (requires stack up; or just confirm compile + that the files don't exist yet). Expected: FAIL (404 / file missing).

- [ ] **Step 3: Create `wwwroot/js/splash-render.js`**

```javascript
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Shared renderer for the Sorcha Wallet PWA loading splash. Pure 2D-canvas
// particle field (the landing-page network aesthetic) with NO DOM access, so
// the identical code runs both on the main thread (fallback) and inside a Web
// Worker against an OffscreenCanvas (keeping the animation smooth while the
// main thread is blocked booting Blazor WASM). Loaded via <script> on the main
// thread and via importScripts() in splash-worker.js.
//
// createSplashRenderer(ctx) -> { resize(width, height, dpr), frame(animate) }
//   ctx     : a CanvasRenderingContext2D or OffscreenCanvasRenderingContext2D
//   resize  : (re)sizes the backing store (via ctx.canvas) and re-seeds peers
//   frame   : renders one frame; pass true to advance the simulation, false
//             for a single static frame (reduced motion)

function createSplashRenderer(ctx) {
  "use strict";
  var w = 0, h = 0, dpr = 1, peers = [], t = 0;
  var C = { r: 99, g: 102, b: 241 };
  var GRID = 38, PEERS = 14, LINK = 95;

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

  function resize(width, height, devicePixelRatio) {
    dpr = devicePixelRatio || 1;
    w = width; h = height;
    ctx.canvas.width = Math.max(1, Math.round(w * dpr));
    ctx.canvas.height = Math.max(1, Math.round(h * dpr));
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    initPeers();
  }

  function frame(animate) {
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

  return { resize: resize, frame: frame };
}
```

- [ ] **Step 4: Create `wwwroot/js/splash-worker.js`**

```javascript
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Web Worker that drives the Sorcha Wallet PWA loading splash animation on a
// background thread via an OffscreenCanvas, so the drifting-blobs animation
// stays smooth even while the main thread is blocked instantiating the Blazor
// WASM runtime. Drawing logic is shared with the main-thread fallback through
// splash-render.js. Only the animated path uses the worker — reduced motion is
// handled inline on the main thread.
//
// Messages in:
//   { type: "init",   canvas: OffscreenCanvas, width, height, dpr }
//   { type: "resize", width, height, dpr }
//   { type: "stop" }

importScripts("splash-render.js");

(function () {
  "use strict";

  var renderer = null;
  var raf = null;

  var schedule = (typeof self.requestAnimationFrame === "function")
    ? function (fn) { return self.requestAnimationFrame(fn); }
    : function (fn) { return self.setTimeout(fn, 16); };
  var unschedule = (typeof self.cancelAnimationFrame === "function")
    ? function (id) { self.cancelAnimationFrame(id); }
    : function (id) { self.clearTimeout(id); };

  function loop() {
    if (!renderer) return;
    renderer.frame(true);
    raf = schedule(loop);
  }

  self.onmessage = function (e) {
    var d = e.data || {};
    if (d.type === "init") {
      var ctx = d.canvas.getContext("2d");
      renderer = createSplashRenderer(ctx);
      renderer.resize(d.width, d.height, d.dpr);
      loop();
    } else if (d.type === "resize") {
      if (renderer) renderer.resize(d.width, d.height, d.dpr);
    } else if (d.type === "stop") {
      if (raf != null) { unschedule(raf); raf = null; }
      self.close();
    }
  };
})();
```

- [ ] **Step 5: Reference `splash-render.js` from index.html**

In `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html`, add the render script immediately BEFORE the existing `js/splash.js` line, so they read:

```html
    <script src="js/splash-render.js"></script>
    <script src="js/splash.js"></script>
```

- [ ] **Step 6: Validate JS syntax**

Run (if node available): `node --check src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash-render.js && node --check src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash-worker.js && echo OK`
Note: `node --check` on `splash-worker.js` parses fine even though `importScripts` is browser-only (it's just an undefined-at-parse identifier, not a syntax error).

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash-render.js \
        src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash-worker.js \
        src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html \
        tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs
git commit -m "feat(pwa): shared splash renderer + offscreen worker (unwired)"
```

---

### Task 2: Wire splash.js to the worker (with fallback)

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js`
- Test: `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `WalletSplashTests` (guards that splash.js actually spawns the worker):

```csharp
    [Test]
    public async Task SplashScript_ReferencesWorker()
    {
        using var http = new HttpClient();
        var js = await http.GetStringAsync(WalletUrl("js/splash.js"));
        Assert.That(js, Does.Contain("splash-worker.js"),
            "splash.js should spawn the OffscreenCanvas worker");
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests.SplashScript_ReferencesWorker" --no-build` (stack up) — Expected: FAIL (current splash.js has no worker reference).

- [ ] **Step 3: Replace `wwwroot/js/splash.js` entirely with this content**

```javascript
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Pre-boot loading splash for the Sorcha Wallet PWA.
//
// Runs before blazor.webassembly.js finishes booting. Renders a lightweight
// network animation (the landing-page hero aesthetic) behind the glowing "S",
// and shows HONEST load feedback:
//
//   Phase 1 (download): a progress bar driven by Blazor's real
//     `--blazor-load-percentage` CSS custom property. Climbs 0 -> 100 truthfully.
//   Handoff: the instant the percentage reaches 100, the bar fades out and
//     indeterminate status text fades in — the number is never seen stalling.
//   Phase 2 (startup tail): shimmering "Starting up… / Almost ready…" until
//     Blazor renders App into #app, which removes this splash.
//
// The canvas animation is rendered on a Web Worker via an OffscreenCanvas
// (splash-worker.js) so the blobs keep moving even while the main thread is
// blocked instantiating the WASM runtime. When OffscreenCanvas/Worker is
// unavailable (older Safari, a CSP block) or reduced motion is requested, it
// falls back to rendering on the main thread via the same shared renderer
// (splash-render.js).
//
// Fallback (progress): if the percentage is never readable, after FALLBACK_MS
// we drop the bar and show "Loading your wallet…" — never a fake number.
//
// Cleanup: a MutationObserver tears down timers, the main-thread rAF, and the
// worker the moment #sorcha-splash leaves the DOM.

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
  var progRaf = null;
  var tailTimer = null;
  var fallbackTimer = null;
  var handedOff = false;

  // ---- canvas animation (worker-preferred, main-thread fallback) ----
  var worker = null;
  var renderer = null;
  var dpr = 1, w = 0, h = 0;

  function measure() {
    dpr = window.devicePixelRatio || 1;
    var r = canvas.getBoundingClientRect();
    w = r.width; h = r.height;
  }

  function startInline() {
    var ctx;
    try { ctx = canvas.getContext("2d"); } catch (e) { ctx = null; }
    if (!ctx || typeof createSplashRenderer !== "function") return;
    renderer = createSplashRenderer(ctx);
    renderer.resize(w, h, dpr);
    if (reduce) {
      renderer.frame(false); // single static frame, no loop
    } else {
      (function loop() {
        renderer.frame(true);
        raf = window.requestAnimationFrame(loop);
      })();
    }
  }

  function startAnimation() {
    measure();
    var canUseWorker = !reduce &&
      typeof OffscreenCanvas !== "undefined" &&
      typeof canvas.transferControlToOffscreen === "function" &&
      typeof Worker !== "undefined";

    if (!canUseWorker) { startInline(); return; }

    try {
      worker = new Worker("js/splash-worker.js");
      var off = canvas.transferControlToOffscreen();
      worker.postMessage(
        { type: "init", canvas: off, width: w, height: h, dpr: dpr },
        [off]);
    } catch (e) {
      // Worker/transfer blocked (e.g. CSP throws synchronously here, before
      // the canvas is transferred) — fall back to main-thread rendering.
      if (worker) { try { worker.terminate(); } catch (e2) {} worker = null; }
      startInline();
    }
  }

  window.addEventListener("resize", function () {
    measure();
    if (worker) {
      worker.postMessage({ type: "resize", width: w, height: h, dpr: dpr });
    } else if (renderer) {
      renderer.resize(w, h, dpr);
      if (reduce) renderer.frame(false);
    }
  });

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
    progRaf = window.requestAnimationFrame(progressLoop);
  }

  // ---- cleanup when Blazor replaces #app ----
  function teardown() {
    if (raf) { window.cancelAnimationFrame(raf); raf = null; }
    if (progRaf) { window.cancelAnimationFrame(progRaf); progRaf = null; }
    if (tailTimer) { clearInterval(tailTimer); tailTimer = null; }
    if (fallbackTimer) { clearTimeout(fallbackTimer); fallbackTimer = null; }
    if (worker) { try { worker.terminate(); } catch (e) {} worker = null; }
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
  startAnimation();

  var FALLBACK_MS = 1500;
  fallbackTimer = window.setTimeout(function () {
    if (!handedOff && readPct() == null) handoff("Loading your wallet…");
  }, FALLBACK_MS);

  progRaf = window.requestAnimationFrame(progressLoop);
})();
```

- [ ] **Step 4: Validate JS syntax**

Run (if node available): `node --check src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js && echo OK`

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/splash.js \
        tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/WalletSplashTests.cs
git commit -m "feat(pwa): render splash animation on OffscreenCanvas worker with fallback"
```

---

## Verification (controller, after both tasks)

1. **Compile:** `dotnet build tests/Sorcha.UI.E2E.Tests/Sorcha.UI.E2E.Tests.csproj` → 0 errors.
2. **Rebuild + serve:** `docker-compose build sorcha-wallet-pwa && docker-compose up -d --no-deps sorcha-wallet-pwa`.
3. **Assets served:** `curl -o /dev/null -w "%{http_code}"` for `/wallet/js/splash-render.js` and `/wallet/js/splash-worker.js` → 200 each.
4. **E2E (Playwright installed locally):** `dotnet test tests/Sorcha.UI.E2E.Tests --filter "WalletSplashTests"` against the warm local stack → all green (8 tests).
5. **Manual smoothness check:** open `/wallet/` with DevTools → Performance/Threads; confirm a dedicated worker is running and the blobs keep moving while the main thread is busy booting WASM. Confirm CSP doesn't block the worker (no `SecurityError` in console; if present, the fallback still animates pre-freeze).

## Deployment (controller, after merge)

Same routine as #1011: confirm Docker Publish on master succeeded, then `az vm run-command` on `sorcha-n1-vm` to `docker compose ... pull sorcha-wallet-pwa` + `up -d --no-deps sorcha-wallet-pwa` (NO `down -v`, NO genesis/validator steps). Verify `https://n1.sorcha.dev/wallet/js/splash-worker.js` → 200.

## Self-Review

- **Spec coverage:** worker rendering (Task 1 worker + Task 2 wiring); shared DRY renderer (Task 1 `splash-render.js`, used by both paths); mandatory fallback (Task 2 `startInline`, CSP/feature-detect/reduced-motion); bar/text/progress unchanged on main thread (Task 2 carries them verbatim); worker teardown (Task 2 `teardown` + MutationObserver); tests for both new scripts served + referenced. ✓
- **Placeholder scan:** none — all code complete.
- **Identifier consistency:** `createSplashRenderer` defined in `splash-render.js`, called in `splash-worker.js` and `splash.js`; message `type`s (`init`/`resize`/`stop`) match between `splash.js` posts and `splash-worker.js` handler; `resize(width,height,dpr)`/`frame(animate)` signatures consistent across all three files; DOM IDs unchanged from the merged feature. ✓
