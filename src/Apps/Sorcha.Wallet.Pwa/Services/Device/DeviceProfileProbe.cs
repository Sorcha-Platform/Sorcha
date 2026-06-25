// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;
using Sorcha.Wallet.Pwa.Models.Device;

namespace Sorcha.Wallet.Pwa.Services.Device;

/// <summary>Raw signals returned by <c>SorchaDeviceProfile.detect()</c> in device-profile.js.</summary>
internal sealed record DeviceSignals(string FormFactor, bool CameraApi, int? HasVideoInput);

/// <summary>
/// JS-interop-backed implementation of <see cref="IDeviceProfileProbe"/>.
/// Calls <c>SorchaDeviceProfile.detect()</c> in <c>device-profile.js</c> to classify
/// form factor and camera availability. Never throws for layout selection — any
/// <see cref="JSException"/> or interop error degrades to a paste-safe profile.
/// </summary>
public sealed class DeviceProfileProbe : IDeviceProfileProbe
{
    private readonly IJSRuntime _js;

    /// <summary>Initialises the probe with the Blazor JS runtime.</summary>
    public DeviceProfileProbe(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<DeviceProfile> GetProfileAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _js.InvokeAsync<DeviceSignals>("SorchaDeviceProfile.detect");

            var formFactor = string.Equals(raw.FormFactor, "handheld", StringComparison.OrdinalIgnoreCase)
                ? DeviceFormFactor.Handheld
                : DeviceFormFactor.Desktop;

            // Camera is usable when the API is present and we haven't confirmed zero video inputs.
            CameraAvailability camera;
            if (!raw.CameraApi)
            {
                camera = CameraAvailability.Unavailable;
            }
            else
            {
                // Optionally refine with enumerateDevices() count (async, non-blocking fallback).
                int? videoCount = null;
                try
                {
                    videoCount = await _js.InvokeAsync<int?>("SorchaDeviceProfile.countVideoInputsAsync");
                }
                catch
                {
                    // enumerateDevices unavailable — rely on cameraApi flag alone.
                }

                camera = videoCount == 0
                    ? CameraAvailability.Unavailable
                    : CameraAvailability.Usable;
            }

            return new DeviceProfile(formFactor, camera);
        }
        catch
        {
            // Any interop failure → paste-safe profile (FR-007, contracts/device-profile.md).
            return new DeviceProfile(DeviceFormFactor.Desktop, CameraAvailability.Unavailable);
        }
    }
}
