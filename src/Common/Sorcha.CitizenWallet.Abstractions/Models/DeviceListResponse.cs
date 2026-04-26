// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Response body for <c>GET /api/v1/wallet/devices</c> and <c>GET /api/v1/me/devices</c>.
/// </summary>
public sealed record DeviceListResponse
{
    /// <summary>All devices enrolled under the authenticated citizen account.</summary>
    public IReadOnlyList<DeviceSummary> Devices { get; init; } = new ReadOnlyCollection<DeviceSummary>([]);
}
