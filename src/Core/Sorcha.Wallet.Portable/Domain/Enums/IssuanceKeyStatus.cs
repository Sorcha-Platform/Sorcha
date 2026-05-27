// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Lifecycle status of a per-organisation VC issuance key (Feature 120 §4 of data-model.md).
/// </summary>
public enum IssuanceKeyStatus
{
    /// <summary>Currently the active issuance key for the org/slot pair.</summary>
    Active = 0,

    /// <summary>Superseded by a newer key (governance op <c>RotateIssuanceKey</c>).</summary>
    Rotated = 1,

    /// <summary>Revoked by governance op <c>VAL_CRED_GOV_001</c>; no further use.</summary>
    Revoked = 2
}
