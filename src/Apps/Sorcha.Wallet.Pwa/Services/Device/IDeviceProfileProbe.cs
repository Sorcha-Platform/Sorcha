// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Pwa.Models.Device;

namespace Sorcha.Wallet.Pwa.Services.Device;

/// <summary>
/// Classifies the holder's device for intake-layout selection on the Present page:
/// form factor (handheld vs desktop) and camera availability. Per-session, never persisted.
/// </summary>
public interface IDeviceProfileProbe
{
    /// <summary>
    /// Probes the current device and returns its <see cref="DeviceProfile"/>, from which the
    /// intake layout (<see cref="IntakeMode"/>) is derived. Must never throw for layout purposes:
    /// any probe failure resolves to a paste-safe profile (camera <c>Unavailable</c>).
    /// </summary>
    ValueTask<DeviceProfile> GetProfileAsync(CancellationToken ct = default);
}
