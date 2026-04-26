// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// IndexedDB bridge for the Sorcha citizen wallet PWA (Feature 114, T054).
// Database name: 'sorcha-wallet', version 1. Five object stores per data-model §B:
//   device       — singleton, key='self'  — keys, wrapping artefacts, enrolment metadata
//   delegation   — singleton, key='self'  — current device delegation JWT
//   credentials  — keyed by id (UUID)     — encrypted SD-JWT VCs
//   statusLists  — keyed by uri           — cached signed status list JWTs
//   syncQueue    — autoincrement key      — pending outbound mutations
//
// Content-key strategy (deviation from data-model §B1, documented in the wallet README):
// v1 uses a non-extractable AES-GCM-256 CryptoKey generated on first run and stored
// directly in IndexedDB (browsers structured-clone CryptoKey natively). XChaCha20-Poly1305
// via libsodium-js is the production target and lands when the libsodium bridge is added;
// the wire-format on disk is incompatible between the two so the schema will rev to v2.
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
  const DB_VERSION = 1;
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
      if (existing && existing.key) return existing.key;
      const key = await crypto.subtle.generateKey(
        { name: "AES-GCM", length: 256 },
        false /* non-extractable — CryptoKey persists via structured clone */,
        ["encrypt", "decrypt"]
      );
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
    const nonce = crypto.getRandomValues(new Uint8Array(12));
    const ciphertext = await crypto.subtle.encrypt(
      { name: "AES-GCM", iv: nonce },
      key,
      new TextEncoder().encode(plaintext)
    );
    return { nonce: bytesToB64Url(nonce), ciphertext: bytesToB64Url(ciphertext) };
  }

  async function decryptString(nonceB64, ciphertextB64) {
    const key = await getOrCreateContentKey();
    const plain = await crypto.subtle.decrypt(
      { name: "AES-GCM", iv: b64UrlToBytes(nonceB64) },
      key,
      b64UrlToBytes(ciphertextB64)
    );
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
