// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Defines the intended purpose of a derived key within the wallet hierarchy.
/// </summary>
public enum KeyUsage
{
    /// <summary>DID identity keys.</summary>
    Identity = 0,

    /// <summary>Verifiable Credential issuance signing.</summary>
    VCIssuance = 1,

    /// <summary>Governance/voting keys.</summary>
    Governance = 2,

    /// <summary>Encrypted communications (X25519 derived).</summary>
    Communications = 3,

    /// <summary>Service-to-service authentication.</summary>
    ServiceAuth = 4
}
