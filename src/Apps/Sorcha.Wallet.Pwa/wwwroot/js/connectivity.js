// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Connectivity bridge for the Sorcha Wallet PWA.
//
// Exposes globalThis.SorchaConnectivity so Blazor can reflect the browser's
// online/offline state into the UI — a wallet should never silently show
// stale, cached data without telling the citizen it is offline.
//
//   register(dotNetRef): boolean
//     Wires window 'online'/'offline' events to invoke
//     dotNetRef.OnConnectivityChanged(isOnline) and returns the CURRENT
//     navigator.onLine value so the caller can paint the right initial state.
//
//   unregister(): void
//     Detaches the listeners. Idempotent.
//
//   isOnline(): boolean
//     Cheap synchronous snapshot of navigator.onLine.
//
// navigator.onLine is a best-effort signal (true == "the browser has a network
// route", not "the Sorcha backend is reachable"), so the C# side treats it as a
// hint and still relies on actual sync outcomes for freshness.

(function () {
  let _ref = null;

  function handler() {
    if (!_ref) return;
    try {
      _ref.invokeMethodAsync("OnConnectivityChanged", !!navigator.onLine);
    } catch {
      /* component disposed between event + dispatch — ignore */
    }
  }

  function register(dotNetRef) {
    _ref = dotNetRef;
    window.addEventListener("online", handler);
    window.addEventListener("offline", handler);
    return !!navigator.onLine;
  }

  function unregister() {
    window.removeEventListener("online", handler);
    window.removeEventListener("offline", handler);
    _ref = null;
  }

  function isOnline() {
    return !!navigator.onLine;
  }

  globalThis.SorchaConnectivity = { register, unregister, isOnline };
})();
