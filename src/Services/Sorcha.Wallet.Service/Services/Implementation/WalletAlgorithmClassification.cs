// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Shared algorithm classification constants for HAIP-facing key derivation.
/// Classical algorithms are those that HAIP 1.0 verifiers can process.
/// </summary>
internal static class WalletAlgorithmClassification
{
    /// <summary>
    /// Algorithms that HAIP 1.0 verifiers can process for holder binding and credential issuance.
    /// </summary>
    internal static readonly HashSet<string> ClassicalAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ED25519", "EDDSA", "ES256", "P-256", "P256", "NIST-P256", "NISTP256", "ECDSA-P256",
        "RS256", "RSA", "RSA-4096"
    };

    /// <summary>
    /// The default classical algorithm used when a PQC-primary wallet needs a classical co-key.
    /// </summary>
    internal const string DefaultClassicalAlgorithm = "ES256";
}
