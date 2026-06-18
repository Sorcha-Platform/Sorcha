// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// PWA install affordance for the Sorcha Verifier (Feature 155). Captures the beforeinstallprompt
// event and exposes a global the install button calls. Registers the service worker.

(function () {
  let deferredPrompt = null;

  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e;
    const btn = document.getElementById('sorcha-install-btn');
    if (btn) btn.style.display = 'inline-flex';
  });

  window.addEventListener('appinstalled', () => {
    deferredPrompt = null;
    const btn = document.getElementById('sorcha-install-btn');
    if (btn) btn.style.display = 'none';
  });

  window.sorchaPromptInstall = async function () {
    if (!deferredPrompt) return 'unavailable';
    deferredPrompt.prompt();
    const { outcome } = await deferredPrompt.userChoice;
    deferredPrompt = null;
    return outcome; // 'accepted' | 'dismissed'
  };

  window.sorchaCanInstall = function () {
    return deferredPrompt !== null;
  };

  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
      navigator.serviceWorker.register('/verify/service-worker.js', { scope: '/verify/' }).catch((err) => {
        console.warn('Verifier service worker registration failed', err);
      });
    });
  }
})();
