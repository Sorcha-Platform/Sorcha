// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Components.Wallet;

/// <summary>
/// Plain-language lifecycle state shown on a <see cref="CredentialWalletCard"/>
/// as a status pill. Leads with status (✓ Valid / Expires soon / Revoked) so a
/// citizen never has to read a raw status-list pointer to know if a card works.
/// </summary>
public enum CredentialStatus
{
    /// <summary>No pill rendered (status unknown / not surfaced).</summary>
    None = 0,

    /// <summary>Active and presentable.</summary>
    Valid = 1,

    /// <summary>Valid but approaching its expiry date.</summary>
    ExpiringSoon = 2,

    /// <summary>Revoked or expired — no longer presentable.</summary>
    Revoked = 3,
}
