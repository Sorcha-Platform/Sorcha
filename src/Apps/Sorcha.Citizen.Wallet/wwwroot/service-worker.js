// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Dev-time service worker — passthrough, no caching. The published variant
// (service-worker.published.js) handles production caching + offline support.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', () => self.clients.claim());
self.addEventListener('fetch', () => { /* network-only in dev */ });
