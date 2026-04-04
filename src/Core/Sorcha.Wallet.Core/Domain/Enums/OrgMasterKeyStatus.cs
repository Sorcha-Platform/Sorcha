// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Lifecycle status of an organisation's master derivation key.
/// </summary>
public enum OrgMasterKeyStatus
{
    /// <summary>Master key in use for derivation.</summary>
    Active = 0,

    /// <summary>Master key replaced (derived keys still valid).</summary>
    Rotated = 1,

    /// <summary>Master key permanently disabled.</summary>
    Revoked = 2
}
