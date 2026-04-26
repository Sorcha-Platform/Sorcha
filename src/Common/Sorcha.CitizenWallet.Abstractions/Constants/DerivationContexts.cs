// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Constants;

/// <summary>
/// Citizen-wallet-specific derivation context identifiers, mirrored from
/// <c>SorchaDerivationPaths</c> in <c>Sorcha.Wallet.Portable</c>.
/// </summary>
/// <remarks>
/// Re-exposed here so the wallet PWA can reference the contexts without
/// taking a dependency on the wallet runtime. Server-side resolution still
/// flows through <c>SorchaDerivationPaths.ResolvePath</c>.
/// </remarks>
public static class DerivationContexts
{
    /// <summary>
    /// Per-PlatformUser citizen wallet holder identity. BIP44 slot 108.
    /// Issuers bind credentials to this key (via the <c>cnf</c> claim) and
    /// it signs device delegation credentials.
    /// </summary>
    public const string CitizenHolder = "sorcha:citizen-holder";

    /// <summary>
    /// Per-org citizen device status-list signing key. BIP44 slot 109.
    /// Signs Token Status List 2024 JWTs that publish citizen device
    /// delegation revocation status.
    /// </summary>
    public const string CitizenStatusSigning = "sorcha:citizen-status-signing";
}
