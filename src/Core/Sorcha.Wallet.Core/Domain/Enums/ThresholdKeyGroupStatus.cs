// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Lifecycle status of a FROST threshold signing key group.
/// </summary>
public enum ThresholdKeyGroupStatus
{
    /// <summary>DKG in progress.</summary>
    Pending = 0,

    /// <summary>Group ready for signing.</summary>
    Active = 1,

    /// <summary>Group permanently disabled.</summary>
    Revoked = 2
}
