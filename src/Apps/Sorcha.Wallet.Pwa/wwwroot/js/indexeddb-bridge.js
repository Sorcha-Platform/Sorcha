// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// IndexedDB bridge for the Sorcha Wallet PWA (Feature 114, T054 — extended by Feature 125).
// Database name: 'sorcha-wallet', version 2.
//   v1 stores (Feature 114):
//     device       — singleton, key='self'  — keys, wrapping artefacts, enrolment metadata
//     delegation   — singleton, key='self'  — current device delegation JWT
//     credentials  — keyed by id (UUID)     — encrypted SD-JWT VCs
//     statusLists  — keyed by uri           — cached signed status list JWTs
//     syncQueue    — autoincrement key      — pending outbound mutations
//   v2 additions (Feature 125 — Sorcha Wallet full user-agent v1):
//     context       — singleton, key='active'    — active org context (per-device)
//     verifications — keyed by id (GUID)         — verification history records
//     personas      — keyed by contextKey string — per-context persona cache
//   v3 additions (Feature 114 US5 — activity log):
//     presentationLog — keyed by id (GUID)       — presentations the citizen has made
//
// Content-key strategy (Feature 114 T056): a 32-byte XChaCha20-Poly1305 content
// key is generated on first run and stored as raw bytes in the `device` store.
// Each credential is sealed with IETF XChaCha20-Poly1305 (24-byte nonce, 16-byte
// tag) via xchacha-bridge.js (@noble/ciphers). Raw key bytes are required because
// the AEAD operates on byte arrays — unlike the earlier non-extractable WebCrypto
// AES-GCM CryptoKey, which couldn't expose its material.
//
// All exports attach to globalThis.SorchaIndexedDb.

(function () {
  if (!globalThis.indexedDB) {
    console.error("[Sorcha] IndexedDB not available — wallet will not persist data.");
    return;
  }
  if (!globalThis.crypto || !globalThis.crypto.subtle) {
    console.error("[Sorcha] WebCrypto not available — credential cache cannot be encrypted.");
    return;
  }

  const DB_NAME = "sorcha-wallet";
  const DB_VERSION = 3;
  const CONTENT_KEY_ID = "content-key/v1";

  let dbPromise = null;

  function openDb() {
    if (dbPromise) return dbPromise;
    dbPromise = new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = (ev) => {
        const db = req.result;
        if (!db.objectStoreNames.contains("device")) db.createObjectStore("device");
        if (!db.objectStoreNames.contains("delegation")) db.createObjectStore("delegation");
        if (!db.objectStoreNames.contains("credentials")) db.createObjectStore("credentials", { keyPath: "id" });
        if (!db.objectStoreNames.contains("statusLists")) db.createObjectStore("statusLists", { keyPath: "uri" });
        if (!db.objectStoreNames.contains("syncQueue")) db.createObjectStore("syncQueue", { keyPath: "id", autoIncrement: true });
        // v2 (Feature 125) — keep these guarded for forward compatibility.
        if (!db.objectStoreNames.contains("context")) db.createObjectStore("context");
        if (!db.objectStoreNames.contains("verifications")) db.createObjectStore("verifications", { keyPath: "id" });
        if (!db.objectStoreNames.contains("personas")) db.createObjectStore("personas");
        // v3 (Feature 114 US5) — citizen's own presentation activity log.
        if (!db.objectStoreNames.contains("presentationLog")) db.createObjectStore("presentationLog", { keyPath: "id" });
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
    return dbPromise;
  }

  function asPromise(req) {
    return new Promise((resolve, reject) => {
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }

  async function put(storeName, value, key) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readwrite");
      const store = tx.objectStore(storeName);
      const req = key === undefined ? store.put(value) : store.put(value, key);
      req.onsuccess = () => resolve();
      req.onerror = () => reject(req.error);
    });
  }

  async function get(storeName, key) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readonly");
      const store = tx.objectStore(storeName);
      const req = store.get(key);
      req.onsuccess = () => resolve(req.result === undefined ? null : req.result);
      req.onerror = () => reject(req.error);
    });
  }

  async function del(storeName, key) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readwrite");
      const store = tx.objectStore(storeName);
      const req = store.delete(key);
      req.onsuccess = () => resolve();
      req.onerror = () => reject(req.error);
    });
  }

  async function getAll(storeName) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readonly");
      const store = tx.objectStore(storeName);
      const req = store.getAll();
      req.onsuccess = () => resolve(req.result || []);
      req.onerror = () => reject(req.error);
    });
  }

  async function clear(storeName) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readwrite");
      const store = tx.objectStore(storeName);
      const req = store.clear();
      req.onsuccess = () => resolve();
      req.onerror = () => reject(req.error);
    });
  }

  // --- content-key + symmetric encryption -----------------------------------

  let contentKeyPromise = null;

  async function getOrCreateContentKey() {
    if (contentKeyPromise) return contentKeyPromise;
    contentKeyPromise = (async () => {
      const existing = await get("device", CONTENT_KEY_ID);
      // Stored as a raw 32-byte key (Uint8Array survives structured-clone).
      if (existing && existing.key instanceof Uint8Array) return existing.key;
      const key = globalThis.SorchaXChaCha.keygen();
      await put("device", { key }, CONTENT_KEY_ID);
      return key;
    })();
    return contentKeyPromise;
  }

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

  async function encryptString(plaintext) {
    const key = await getOrCreateContentKey();
    const nonce = globalThis.SorchaXChaCha.nonce();
    const ciphertext = await globalThis.SorchaXChaCha.encrypt(
      key, nonce, new TextEncoder().encode(plaintext));
    return { nonce: bytesToB64Url(nonce), ciphertext: bytesToB64Url(ciphertext) };
  }

  async function decryptString(nonceB64, ciphertextB64) {
    const key = await getOrCreateContentKey();
    const plain = await globalThis.SorchaXChaCha.decrypt(
      key, b64UrlToBytes(nonceB64), b64UrlToBytes(ciphertextB64));
    return new TextDecoder().decode(plain);
  }

  // --- credentials ----------------------------------------------------------
  // Encrypts an opaque payload (callers serialise their own object to JSON first).

  async function putCredential(id, payloadJson) {
    const { nonce, ciphertext } = await encryptString(payloadJson);
    await put("credentials", { id, nonce, ciphertext, cachedAt: new Date().toISOString() });
  }

  async function getCredential(id) {
    const row = await get("credentials", id);
    if (!row) return null;
    return await decryptString(row.nonce, row.ciphertext);
  }

  async function listCredentials() {
    const rows = await getAll("credentials");
    const out = [];
    for (const row of rows) {
      out.push(await decryptString(row.nonce, row.ciphertext));
    }
    return out;
  }

  async function deleteCredential(id) {
    await del("credentials", id);
  }

  globalThis.SorchaIndexedDb = {
    // raw store ops
    put, get, del, getAll, clear,
    // credentials (encrypted)
    putCredential, getCredential, listCredentials, deleteCredential,
  };
})();
