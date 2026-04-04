// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Determines how key material is stored and who holds custody.
/// </summary>
public enum CustodyMode
{
    /// <summary>Full key in server storage, encrypted at rest.</summary>
    Custodial = 0,

    /// <summary>Server share + device share (future).</summary>
    CoSigned = 1,

    /// <summary>Full key on device, optional recovery escrow (future).</summary>
    SelfCustody = 2
}
