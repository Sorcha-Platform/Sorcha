// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Tracks the progression of a FROST threshold signing session.
/// </summary>
public enum SigningSessionState
{
    /// <summary>Session created, awaiting participants.</summary>
    Initializing = 0,

    /// <summary>First round of FROST protocol.</summary>
    Round1 = 1,

    /// <summary>Second round of FROST protocol.</summary>
    Round2 = 2,

    /// <summary>Signing succeeded.</summary>
    Complete = 3,

    /// <summary>Signing failed or timed out.</summary>
    Failed = 4
}
