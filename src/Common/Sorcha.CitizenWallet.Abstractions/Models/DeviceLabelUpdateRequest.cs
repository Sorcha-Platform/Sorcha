// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Request body for <c>PUT /api/v1/wallet/devices/{deviceId}/label</c>.
/// </summary>
public sealed record DeviceLabelUpdateRequest
{
    /// <summary>New citizen-visible label, 1..120 chars.</summary>
    public string Label { get; init; } = string.Empty;
}
