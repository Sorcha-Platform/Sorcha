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

  // ---------------------------------------------------------------------------
  // ECDH P-256 — the mdoc proximity device key (Feature 185).
  //
  // WHY A SECOND KEY EXISTS. ISO 18013-5 derives EMacKey by ECDH between the mdoc's STATIC device key
  // (published in the MSO) and the reader's EPHEMERAL key — so the MSO device key must be ECDH-capable.
  // In WebCrypto a key's usages are FIXED AT GENERATION and a key cannot be both ECDSA and ECDH. The
  // ECDSA key above is "sign"-only and therefore structurally cannot produce a deviceMac, no matter what
  // we do to it. Hence a distinct key, with distinct usages.
  //
  // Both keys stay NON-EXTRACTABLE. That is why the ECDH itself has to happen here rather than in C#:
  // the private key never leaves WebCrypto, so .NET can never hold it. deriveBits hands back the raw
  // shared secret (the P-256 x-coordinate, 32 bytes) — exactly the HKDF input keying material the
  // standard calls Z — and C# does the HKDF over that.

  async function generateEcdhP256(id) {
    if (keys.has(id)) return id;
    const pair = await subtle.generateKey(
      { name: "ECDH", namedCurve: "P-256" },
      false /* non-extractable private key */,
      ["deriveBits"]   // NOT "sign" — an ECDH key cannot sign, and that is fine: mdoc uses one or the other
    );
    const publicJwk = await subtle.exportKey("jwk", pair.publicKey);
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

  /**
   * Performs the ECDH agreement between this device's private key and a peer's public key.
   *
   * @param id            the ECDH key id
   * @param peerJwkJson   the peer's public key as a JWK (JSON string)
   * @returns             the raw shared secret (Z), base64url — 32 bytes for P-256
   */
  async function deriveSharedSecret(id, peerJwkJson) {
    const entry = keys.get(id);
    if (!entry) throw new Error(`No key with id '${id}'`);

    const peerJwk = JSON.parse(peerJwkJson);
    const peerKey = await subtle.importKey(
      "jwk",
      { kty: peerJwk.kty, crv: peerJwk.crv, x: peerJwk.x, y: peerJwk.y },
      { name: "ECDH", namedCurve: "P-256" },
      false,
      []   // a public key needs no usages
    );

    // 256 bits = the P-256 x-coordinate. This is Z, the HKDF input keying material.
    const bits = await subtle.deriveBits(
      { name: "ECDH", public: peerKey },
      entry.privateKey,
      256
    );

    return bytesToB64Url(bits);
  }

  function disposeKey(id) {
    keys.delete(id);
  }

  globalThis.SorchaWebCrypto = {
    generateEcdsaP256,
    generateEcdhP256,
    deriveSharedSecret,
    getPublicJwk,
    getThumbprint,
    signEs256,
    disposeKey,
  };
})();
