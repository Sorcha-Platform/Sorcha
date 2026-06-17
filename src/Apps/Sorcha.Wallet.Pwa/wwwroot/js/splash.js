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
  var progRaf = null;
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
    progRaf = window.requestAnimationFrame(progressLoop);
  }

  // ---- cleanup when Blazor replaces #app ----
  function teardown() {
    if (raf) { window.cancelAnimationFrame(raf); raf = null; }
    if (progRaf) { window.cancelAnimationFrame(progRaf); progRaf = null; }
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
  window.addEventListener("resize", function () { size(); initPeers(); if (reduce) draw(false); });

  // Fallback: no readable percentage within FALLBACK_MS -> indeterminate text.
  var FALLBACK_MS = 1500;
  fallbackTimer = window.setTimeout(function () {
    if (!handedOff && readPct() == null) handoff("Loading your wallet…");
  }, FALLBACK_MS);

  progRaf = window.requestAnimationFrame(progressLoop);
})();
