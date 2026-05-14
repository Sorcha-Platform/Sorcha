// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Production service worker — minimal placeholder.
// Real precache + offline strategy lands with T071 (Phase 2) when the PWA
// runtime services come online.
//
// Caution: changes here are picked up by the Blazor build via the
// service-worker-assets.js manifest, so even minor edits invalidate every
// installed PWA's cache on next visit.

self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

// Feature 114 / US4 — Background Sync wakes a closed/backgrounded PWA when a
// CredentialAvailable hub push fires while the foreground tab is hidden. The
// page registers tag 'citizen-credential-sync' on hub events; the handler here
// notifies any open clients to re-run their sync. Chromium-only — non-Chromium
// browsers fire the registration call but the event never arrives, so the
// next foreground open + pull-on-load path is the unconditional fallback.
self.addEventListener('sync', event => {
    if (event.tag === 'citizen-credential-sync') {
        event.waitUntil(notifyClientsForSync());
    }
});

async function notifyClientsForSync() {
    const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of clients) {
        client.postMessage({ kind: 'citizen-credential-sync' });
    }
}

const cacheNamePrefix = 'sorcha-wallet-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

async function onInstall(event) {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(request) {
    let cachedResponse = null;
    if (request.method === 'GET') {
        const shouldServeIndexHtml = request.mode === 'navigate';
        const requestKey = shouldServeIndexHtml ? 'index.html' : request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(requestKey);
    }
    return cachedResponse || fetch(request);
}
