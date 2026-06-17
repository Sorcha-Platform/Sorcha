// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Service worker for the Sorcha Verifier PWA (Feature 155, path A).
// This is a Blazor *Server* app — the interactive circuit cannot be cached, so the worker only
// gives the app installability + an offline-fallback shell. It deliberately does NOT cache the host
// page or framework files as immutable (they change every build — the wallet-PWA cache lesson).

const CACHE = 'sorcha-verifier-shell-v1';
const OFFLINE_URL = '/verify/offline.html';

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE).then((cache) => cache.addAll([OFFLINE_URL, '/verify/manifest.webmanifest', '/verify/icons/icon.svg']))
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
  );
  self.clients.claim();
});

// Network-first for navigations; fall back to the offline shell when disconnected. Never cache the
// circuit/API responses — this verifier consults live public data.
self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.mode === 'navigate') {
    event.respondWith(fetch(req).catch(() => caches.match(OFFLINE_URL)));
    return;
  }
  if (req.url.endsWith('/icons/icon.svg') || req.url.endsWith('/manifest.webmanifest')) {
    event.respondWith(caches.match(req).then((hit) => hit || fetch(req)));
  }
});
