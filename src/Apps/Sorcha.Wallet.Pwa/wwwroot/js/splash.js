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
