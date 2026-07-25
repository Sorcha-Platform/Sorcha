// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Feature 128 US3 — PWA install probe.
//
// Captures the `beforeinstallprompt` event eagerly on module load so
// the C# probe can resolve quickly when invoked from a Razor component.
// Per research R2: Chromium fires `beforeinstallprompt` within a few
// hundred ms of page load if eligibility checks pass; iOS Safari never
// fires it (we detect that via UA-string fallback).

let deferredPrompt = null;

window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e;
});

window.addEventListener('appinstalled', () => {
    deferredPrompt = null;
});

function isIosSafari() {
    const ua = navigator.userAgent || '';
    // iOS Safari ≥16.4: PWA installable via Add-to-Home-Screen but no
    // programmatic install API. Detect iPhone/iPad UA + Safari (exclude
    // Chrome-on-iOS and other webviews which DON'T support PWA install).
    const isIos = /iPad|iPhone|iPod/.test(ua) && !window.MSStream;
    const isSafari = /Safari/.test(ua) && !/CriOS|FxiOS|EdgiOS|GSA/.test(ua);
    return isIos && isSafari;
}

function isAlreadyInstalled() {
    // If the page is running inside an installed PWA (standalone
    // display mode), there's nothing to install.
    return window.matchMedia('(display-mode: standalone)').matches
        || window.navigator.standalone === true;
}

// Issue #1270: the surface previously had only one axis — installable or not —
// and treated "not installable" as "this is a desktop". Mobile-but-not-installable
// is neither: an in-app browser, a non-Safari iOS browser, or an Android device
// that ALREADY installed the wallet (so `beforeinstallprompt` never fires again)
// all land there, and were shown a QR code to scan with the phone in their hand.
// Coarse on purpose — this only decides which affordance leads, never anything
// security-relevant, so a pointer/UA heuristic is proportionate.
export function isMobile() {
    if (window.matchMedia('(pointer: coarse)').matches
        && window.matchMedia('(max-width: 820px)').matches) {
        return true;
    }
    const ua = navigator.userAgent || '';
    return /Android|iPad|iPhone|iPod|Mobile|Silk|Kindle/i.test(ua);
}

export async function probe() {
    if (isAlreadyInstalled()) {
        return 'installed';
    }

    // Wait briefly for the beforeinstallprompt event in case the page
    // just loaded and Chromium hasn't fired it yet.
    if (!deferredPrompt) {
        await new Promise(resolve => setTimeout(resolve, 500));
    }

    if (deferredPrompt) {
        return 'programmatic';
    }

    if (isIosSafari()) {
        return 'manual';
    }

    return 'none';
}

export async function triggerInstall() {
    if (!deferredPrompt) {
        return false;
    }

    try {
        deferredPrompt.prompt();
        const choice = await deferredPrompt.userChoice;
        deferredPrompt = null;
        return choice && choice.outcome === 'accepted';
    } catch (e) {
        return false;
    }
}
