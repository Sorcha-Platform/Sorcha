// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Constants;

/// <summary>
/// Predefined Sorcha system derivation paths for specific operations
/// </summary>
/// <remarks>
/// These constants define standard BIP44 paths for common Sorcha operations.
/// Using predefined paths ensures consistency across the system and allows
/// for controlled key derivation for specific purposes.
///
/// Path format: m/44'/0'/0'/0/{index}
/// - 44' = BIP44 purpose (hardened)
/// - 0' = Coin type (0 for Bitcoin/generic, hardened)
/// - 0' = Account 0 (hardened)
/// - 0 = External chain (receive addresses)
/// - {index} = Address index for specific Sorcha operations
/// </remarks>
public static class SorchaDerivationPaths
{
    /// <summary>
    /// System path prefix for Sorcha-defined paths
    /// </summary>
    public const string SystemPrefix = "sorcha:";

    /// <summary>
    /// Derivation path for register attestation signing
    /// </summary>
    /// <remarks>
    /// Used when owners/admins sign attestations to approve register creation.
    /// Maps to: m/44'/0'/0'/0/100
    /// </remarks>
    public const string RegisterAttestation = "sorcha:register-attestation";

    /// <summary>
    /// BIP44 path for register attestation signing
    /// </summary>
    public const string RegisterAttestationPath = "m/44'/0'/0'/0/100";

    /// <summary>
    /// Derivation path for control record signing (system wallet only)
    /// </summary>
    /// <remarks>
    /// Used by the Validator service system wallet to sign complete control records
    /// after all attestations are collected.
    /// Maps to: m/44'/0'/0'/0/101
    /// </remarks>
    public const string RegisterControl = "sorcha:register-control";

    /// <summary>
    /// BIP44 path for control record signing
    /// </summary>
    public const string RegisterControlPath = "m/44'/0'/0'/0/101";

    /// <summary>
    /// Derivation path for docket signing (system wallet only)
    /// </summary>
    /// <remarks>
    /// Used by the Validator service to sign dockets after transaction validation.
    /// Maps to: m/44'/0'/0'/0/102
    /// </remarks>
    public const string DocketSigning = "sorcha:docket-signing";

    /// <summary>
    /// BIP44 path for docket signing
    /// </summary>
    public const string DocketSigningPath = "m/44'/0'/0'/0/102";

    /// <summary>
    /// Derivation path for blueprint publishing to the system register
    /// </summary>
    /// <remarks>
    /// Used by the Register Service system wallet to sign blueprint transactions
    /// published to the system register.
    /// Maps to: m/44'/0'/0'/0/103
    /// </remarks>
    public const string BlueprintPublish = "sorcha:blueprint-publish";

    /// <summary>
    /// BIP44 path for blueprint publishing
    /// </summary>
    public const string BlueprintPublishPath = "m/44'/0'/0'/0/103";

    /// <summary>
    /// Resolves a Sorcha system path to its corresponding BIP44 path
    /// </summary>
    /// <param name="systemPath">Sorcha system path (e.g., "sorcha:register-attestation")</param>
    /// <returns>BIP44 derivation path string</returns>
    /// <exception cref="ArgumentException">Thrown when system path is not recognized</exception>
    public static string ResolvePath(string systemPath)
    {
        if (string.IsNullOrWhiteSpace(systemPath))
            throw new ArgumentException("System path cannot be empty", nameof(systemPath));

        // If it's already a BIP44 path, return as-is
        if (systemPath.StartsWith("m/", StringComparison.OrdinalIgnoreCase))
            return systemPath;

        // Resolve Sorcha system paths
        return systemPath.ToLowerInvariant() switch
        {
            RegisterAttestation => RegisterAttestationPath,
            RegisterControl => RegisterControlPath,
            DocketSigning => DocketSigningPath,
            BlueprintPublish => BlueprintPublishPath,
            _ => throw new ArgumentException($"Unknown Sorcha system path: {systemPath}", nameof(systemPath))
        };
    }

    /// <summary>
    /// Checks if a path string is a Sorcha system path
    /// </summary>
    /// <param name="path">Path to check</param>
    /// <returns>True if the path is a Sorcha system path</returns>
    public static bool IsSystemPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               path.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
