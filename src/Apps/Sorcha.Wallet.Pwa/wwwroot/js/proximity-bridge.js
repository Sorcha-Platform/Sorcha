// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Bridges the native sorcha-proximity Capacitor plugin to Blazor.
//
// Shape follows SorchaConnectivity (the only push-from-JS pattern in this wallet): C# hands us a
// DotNetObjectReference, and we invoke [JSInvokable] methods on it when the native side raises an event.
//
// Bytes cross as base64 strings, never as byte[] — same as SorchaWebCrypto and SorchaIndexedDb.
//
// The plugin is ABSENT in a plain browser (capacitor.config.json uses server.url, so the very same build
// really does run there). Everything here must therefore degrade rather than throw: `probe` answers honestly
// and the UI hides in-person sharing. See IProximityTransport.ProbeAsync.

(function () {
    'use strict';

    let dotNetRef = null;
    let listeners = [];

    function plugin() {
        return globalThis.Capacitor?.Plugins?.SorchaProximity ?? null;
    }

    globalThis.SorchaProximity = {

        /** True only inside the native app. False in a browser — and that is a normal, expected answer. */
        isAvailable: function () {
            return plugin() !== null;
        },

        probe: async function () {
            const p = plugin();
            if (!p) {
                // Not an error. The wallet is a PWA and genuinely runs in browsers; the UI needs a truthful
                // "no" so it can hide the affordance instead of offering something broken.
                return JSON.stringify({
                    supported: false,
                    bluetoothEnabled: false,
                    permissionGranted: false,
                    reason: 'noPlugin'
                });
            }
            return JSON.stringify(await p.probe());
        },

        /** Registers the C# side for `received` / `disconnected` callbacks. */
        register: async function (ref) {
            const p = plugin();
            if (!p) return false;

            await globalThis.SorchaProximity.unregister();
            dotNetRef = ref;

            listeners.push(await p.addListener('received', function (e) {
                dotNetRef?.invokeMethodAsync('OnReceived', e.dataBase64);
            }));

            listeners.push(await p.addListener('disconnected', function (e) {
                dotNetRef?.invokeMethodAsync('OnDisconnected', e.reason);
            }));

            return true;
        },

        unregister: async function () {
            for (const listener of listeners) {
                try {
                    await listener.remove();
                } catch {
                    // Already gone. Tearing down must never itself fail — sessions abandon from arbitrary
                    // states (screen lock, walking away).
                }
            }
            listeners = [];
            dotNetRef = null;
        },

        startPeripheral: async function (serviceUuid) {
            const p = plugin();
            if (!p) throw new Error('In-person sharing needs the Sorcha app.');
            await p.startPeripheral({ serviceUuid: serviceUuid });
        },

        connectCentral: async function (serviceUuid) {
            const p = plugin();
            if (!p) throw new Error('In-person reading needs the Sorcha app.');
            await p.connectCentral({ serviceUuid: serviceUuid });
        },

        send: async function (dataBase64) {
            const p = plugin();
            if (!p) throw new Error('No proximity transport.');
            await p.send({ dataBase64: dataBase64 });
        },

        stop: async function () {
            const p = plugin();
            if (!p) return;
            try {
                await p.stop();
            } catch {
                // Idempotent and safe from any state.
            }
        }
    };
})();
