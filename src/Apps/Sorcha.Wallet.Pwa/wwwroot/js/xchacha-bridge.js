// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// XChaCha20-Poly1305 AEAD bridge for the Sorcha Wallet PWA (Feature 114, T056).
//
// Fulfils the T056 "libsodium-bridge" slot using @noble/ciphers instead of
// libsodium-wrappers: same IETF XChaCha20-Poly1305 wire format (24-byte nonce,
// 16-byte tag) at ~20 KB of pure-JS vs ~800 KB of WASM, and no extra CSP
// (no wasm-unsafe-eval / WASM precache needed).
//
// Exposes a small same-JS-context API on globalThis.SorchaXChaCha consumed by
// indexeddb-bridge.js. All byte arguments/returns are Uint8Array; nothing
// crosses the Blazor JSInterop boundary here (only the IndexedDB string-level
// putCredential/getCredential do).
//
//   keygen(): Uint8Array(32)              — fresh content key
//   nonce():  Uint8Array(24)              — fresh per-message nonce
//   encrypt(key, nonce, data, aad?): Promise<Uint8Array>  — ciphertext+tag
//   decrypt(key, nonce, ct,   aad?): Promise<Uint8Array>  — plaintext
//
// The noble library is lazy-loaded as an ES module on first use and memoised.

(function () {
  let _cipherPromise = null;

  function loadCipher() {
    if (!_cipherPromise) {
      // Resolve against document.baseURI (<base href="/wallet/">) so the dynamic
      // import works regardless of how the browser bases a classic script's import().
      const url = new URL("js/vendor/noble-ciphers/chacha.js", document.baseURI).href;
      _cipherPromise = import(url).then((m) => m.xchacha20poly1305);
    }
    return _cipherPromise;
  }

  function keygen() {
    return crypto.getRandomValues(new Uint8Array(32));
  }

  function nonce() {
    return crypto.getRandomValues(new Uint8Array(24));
  }

  async function encrypt(key, nonceBytes, data, aad) {
    const xchacha = await loadCipher();
    const aead = aad
      ? xchacha(key, nonceBytes, aad)
      : xchacha(key, nonceBytes);
    return aead.encrypt(data);
  }

  async function decrypt(key, nonceBytes, ciphertext, aad) {
    const xchacha = await loadCipher();
    const aead = aad
      ? xchacha(key, nonceBytes, aad)
      : xchacha(key, nonceBytes);
    return aead.decrypt(ciphertext);
  }

  globalThis.SorchaXChaCha = { keygen, nonce, encrypt, decrypt };
})();
