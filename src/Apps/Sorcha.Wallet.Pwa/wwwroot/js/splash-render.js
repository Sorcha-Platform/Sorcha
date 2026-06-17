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
