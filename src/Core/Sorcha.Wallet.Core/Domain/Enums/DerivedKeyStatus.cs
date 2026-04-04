// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Lifecycle status of a derived key within the wallet hierarchy.
/// </summary>
public enum DerivedKeyStatus
{
    /// <summary>Key can sign and decrypt.</summary>
    Active = 0,

    /// <summary>Key can decrypt only (signing disabled).</summary>
    Rotated = 1,

    /// <summary>Key fully disabled.</summary>
    Revoked = 2
}
