// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// QR scanner bridge for the Sorcha Wallet PWA (Feature 114, T057).
//
// Exposes a one-shot, Promise-based API onto globalThis.SorchaQrScanner so
// Blazor C# can `await JSRuntime.InvokeAsync<string>("SorchaQrScanner.start", ...)`
// without DotNetObjectReference plumbing.
//
//   start(videoElementId): Promise<string>
//     Mounts the qr-scanner library against the given <video> element,
//     starts the camera, and resolves with the first detected QR payload.
//     Rejects with the string 'cancelled' if stop() is called before a
//     detection, or with an error message on permission denial / no camera.
//
//   stop(): void
//     Halts the active scan. Idempotent. Causes any pending start() promise
//     to reject with 'cancelled'.
//
//   isSupported(): boolean
//     Cheap synchronous check — true when the browser exposes
//     navigator.mediaDevices.getUserMedia.
//
// The qr-scanner library itself is loaded lazily as an ES module the first
// time start() is called, so the ~16 KB main + ~44 KB worker only download
// when the user actually opens the scan UI.

(function () {
  let _qrScannerCtorPromise = null;
  let _scanner = null;
  let _pendingReject = null;

  function isSupported() {
    return !!(globalThis.navigator
      && navigator.mediaDevices
      && typeof navigator.mediaDevices.getUserMedia === "function");
  }

  function loadQrScanner() {
    if (!_qrScannerCtorPromise) {
      // Resolve against document.baseURI (<base href="/wallet/">) so the dynamic
      // import + worker path work regardless of how the browser bases a classic
      // script's import(). Same-origin paths satisfy CSP `script-src 'self'` /
      // `worker-src 'self' blob:`.
      const moduleUrl = new URL("js/vendor/qr-scanner/qr-scanner.min.js", document.baseURI).href;
      const workerUrl = new URL("js/vendor/qr-scanner/qr-scanner-worker.min.js", document.baseURI).href;
      _qrScannerCtorPromise = import(moduleUrl).then((m) => {
        const Ctor = m.default;
        Ctor.WORKER_PATH = workerUrl;
        return Ctor;
      });
    }
    return _qrScannerCtorPromise;
  }

  async function start(videoElementId) {
    if (!isSupported()) {
      throw new Error("camera-unsupported");
    }
    if (_scanner) {
      // Defensive: a previous scan was never cleaned up. Tear it down before
      // starting fresh.
      stop();
    }

    const QrScanner = await loadQrScanner();
    const video = document.getElementById(videoElementId);
    if (!video) {
      throw new Error("video-element-not-found");
    }

    return new Promise((resolve, reject) => {
      let settled = false;

      const settleResolve = (value) => {
        if (settled) return;
        settled = true;
        _pendingReject = null;
        try { _scanner?.stop(); _scanner?.destroy(); } catch { /* ignore */ }
        _scanner = null;
        resolve(value);
      };
      const settleReject = (reason) => {
        if (settled) return;
        settled = true;
        _pendingReject = null;
        try { _scanner?.stop(); _scanner?.destroy(); } catch { /* ignore */ }
        _scanner = null;
        reject(reason);
      };

      const onDetect = (result) => {
        // qr-scanner 1.4.x callback signature: `{data, cornerPoints}`; older
        // builds passed just the string. Handle both.
        const text = typeof result === "string" ? result : (result && result.data) || "";
        if (!text) return;
        settleResolve(text);
      };

      try {
        _scanner = new QrScanner(video, onDetect, {
          highlightScanRegion: true,
          highlightCodeOutline: true,
          preferredCamera: "environment",
          maxScansPerSecond: 10,
        });
      } catch (err) {
        settleReject(err && err.message ? err.message : String(err));
        return;
      }

      _pendingReject = () => settleReject("cancelled");

      _scanner.start().catch((err) => {
        // getUserMedia rejection lands here — permission denied, no camera, etc.
        settleReject(err && err.message ? err.message : String(err));
      });
    });
  }

  function stop() {
    if (_pendingReject) {
      _pendingReject();
    } else if (_scanner) {
      // No outstanding promise but a scanner is mounted — tear down anyway.
      try { _scanner.stop(); _scanner.destroy(); } catch { /* ignore */ }
      _scanner = null;
    }
  }

  globalThis.SorchaQrScanner = { start, stop, isSupported };
})();
