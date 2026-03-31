// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Components.Wallet;

/// <summary>
/// Transaction lifecycle tick status for UI rendering.
/// Maps to the WhatsApp-style delivery indicator pattern.
/// </summary>
public enum TransactionTickStatus
{
    /// <summary>Submitted, not yet sealed (single grey tick).</summary>
    Pending,

    /// <summary>Sealed in docket (single blue tick).</summary>
    Sealed,

    /// <summary>Receipt confirmed — cryptographic proof of finality (double blue ticks).</summary>
    Receipted
}
