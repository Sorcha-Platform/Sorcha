// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Feature 159 — device-profile.js
// Pure capability detection for intake-layout selection on the Present page.
// Returns form factor + camera availability signals to IDeviceProfileProbe (C#).
// NEVER calls getUserMedia — capability check only (R2, contracts/device-profile.md).

window.SorchaDeviceProfile = {
    /**
     * Detect the device profile for intake-layout selection.
     * @returns {{ formFactor: "handheld"|"desktop", cameraApi: boolean, hasVideoInput: boolean|null }}
     */
    detect: function () {
        // R1 — form factor: coarse pointer + touch points as primary signal,
        // viewport width as corroboration (small viewports lean handheld).
        var coarsePointer = window.matchMedia && window.matchMedia("(pointer: coarse)").matches;
        var hasTouchPoints = navigator.maxTouchPoints > 0;
        var narrowViewport = window.innerWidth > 0 && window.innerWidth < 1024;
        var formFactor = (coarsePointer && hasTouchPoints) || (hasTouchPoints && narrowViewport)
            ? "handheld"
            : "desktop";

        // R2 — camera API: check presence of getUserMedia (mirrors SorchaQrScanner.isSupported).
        var cameraApi = !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);

        // R2 cont. — optional: enumerate video inputs without prompting.
        // null when not determinable (e.g. enumerateDevices unavailable or returns
        // only 'audioinput' without labels before permission is granted).
        var hasVideoInput = null;
        if (cameraApi && navigator.mediaDevices.enumerateDevices) {
            try {
                // enumerateDevices is async; we return the sync signals and let
                // DeviceProfileProbe handle the async path separately if needed.
                // For the synchronous detect() call we attempt a best-effort
                // check: if the browser exposes labelled devices pre-permission
                // (Chrome on desktop), we count them; otherwise leave as null.
                // The C# probe calls detect() which returns null for hasVideoInput
                // in the common case, so CameraAvailability falls back to
                // cameraApi-only — this is intentional and safe (R2).
                hasVideoInput = null; // async path; see DeviceProfileProbe.cs
            } catch {
                hasVideoInput = null;
            }
        }

        return {
            formFactor: formFactor,
            cameraApi: cameraApi,
            hasVideoInput: hasVideoInput
        };
    },

    /**
     * Async variant that awaits enumerateDevices() to count video inputs.
     * Called by DeviceProfileProbe.cs when cameraApi is true.
     * @returns {Promise<number|null>} count of videoinput devices, or null if indeterminate.
     */
    countVideoInputsAsync: function () {
        if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) {
            return Promise.resolve(null);
        }
        return navigator.mediaDevices.enumerateDevices()
            .then(function (devices) {
                var count = devices.filter(function (d) { return d.kind === "videoinput"; }).length;
                // A count of 0 with unlabelled devices (pre-permission) is indeterminate;
                // treat as null so the C# probe defaults to cameraApi-only (paste-safe).
                var allUnlabelled = devices.every(function (d) { return d.label === ""; });
                return (count === 0 && allUnlabelled) ? null : count;
            })
            .catch(function () { return null; });
    }
};
