// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// WebCrypto bridge for the Sorcha citizen wallet PWA (Feature 114, T055).
// Holds non-extractable EC P-256 CryptoKey objects in a process-global Map
// keyed by string id, since CryptoKey instances cannot cross the JS-interop
// boundary into .NET. The wallet refers to keys by id.
//
// All exports are attached to globalThis.SorchaWebCrypto so they are reachable
// via IJSRuntime.InvokeAsync("SorchaWebCrypto.<fn>", ...).

(function () {
  const subtle = (globalThis.crypto && globalThis.crypto.subtle) || null;
  if (!subtle) {
    console.error("[Sorcha] WebCrypto not available — wallet will not function.");
    return;
  }

  const keys = new Map(); // id -> { privateKey, publicKey, publicJwk, thumbprint }

  function bytesToB64Url(buf) {
    const bytes = new Uint8Array(buf);
    let s = "";
    for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
    return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }

  function b64UrlToBytes(s) {
    s = s.replace(/-/g, "+").replace(/_/g, "/");
    while (s.length % 4) s += "=";
    const bin = atob(s);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
  }

  async function rfc7638Thumbprint(jwk) {
    // Canonical: {"crv":"...","kty":"...","x":"...","y":"..."}
    const canonical = `{"crv":"${jwk.crv}","kty":"${jwk.kty}","x":"${jwk.x}","y":"${jwk.y}"}`;
    const hash = await subtle.digest("SHA-256", new TextEncoder().encode(canonical));
    return bytesToB64Url(hash);
  }

  async function generateEcdsaP256(id) {
    if (keys.has(id)) return id;
    const pair = await subtle.generateKey(
      { name: "ECDSA", namedCurve: "P-256" },
      false /* non-extractable for the private key */,
      ["sign", "verify"]
    );
    const publicJwk = await subtle.exportKey("jwk", pair.publicKey);
    // Strip key_ops/ext for clean JWK on the wire.
    const cleanJwk = { kty: publicJwk.kty, crv: publicJwk.crv, x: publicJwk.x, y: publicJwk.y };
    const thumbprint = await rfc7638Thumbprint(cleanJwk);
    keys.set(id, {
      privateKey: pair.privateKey,
      publicKey: pair.publicKey,
      publicJwk: cleanJwk,
      thumbprint,
    });
    return id;
  }

  function getPublicJwk(id) {
    const entry = keys.get(id);
    if (!entry) throw new Error(`No key with id '${id}'`);
    return JSON.stringify(entry.publicJwk);
  }

  function getThumbprint(id) {
    const entry = keys.get(id);
    if (!entry) throw new Error(`No key with id '${id}'`);
    return entry.thumbprint;
  }

  async function signEs256(id, dataB64Url) {
    const entry = keys.get(id);
    if (!entry) throw new Error(`No key with id '${id}'`);
    const data = b64UrlToBytes(dataB64Url);
    const sig = await subtle.sign(
      { name: "ECDSA", hash: "SHA-256" },
      entry.privateKey,
      data
    );
    // WebCrypto returns IEEE-P1363 fixed r||s (64 bytes for P-256). .NET ECDsa.VerifyData
    // defaults to the same format, so no conversion needed.
    return bytesToB64Url(sig);
  }

  globalThis.SorchaWebCrypto = {
    generateEcdsaP256,
    getPublicJwk,
    getThumbprint,
    signEs256,
  };
})();
