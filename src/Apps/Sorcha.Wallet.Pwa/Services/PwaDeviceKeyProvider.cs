// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.UI.Core.Services.DeviceKeys;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Citizen OID4VP device-bound <c>cnf</c> (Phase 1, #1195) — PWA implementation of the host-neutral
/// <see cref="IDeviceKeyProvider"/> seam. Adapts the PWA's <see cref="IDeviceKeyService"/> so the
/// shared <c>DeviceKeyRenderer</c> can bind the SD-JWT <c>cnf</c> to this device's public signing JWK.
/// </summary>
public sealed class PwaDeviceKeyProvider(IDeviceKeyService deviceKeys) : IDeviceKeyProvider
{
    /// <inheritdoc />
    public async Task<JsonElement?> GetDevicePublicJwkAsync(CancellationToken ct = default)
        => await deviceKeys.GetPublicJwkAsync(ct);
}
