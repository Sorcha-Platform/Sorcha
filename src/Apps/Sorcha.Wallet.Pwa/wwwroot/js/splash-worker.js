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
