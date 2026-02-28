// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Enums;

namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Centralized mapping between algorithm name strings and <see cref="WalletNetworks"/> enum values.
/// </summary>
/// <remarks>
/// <para>
/// This utility consolidates algorithm-to-enum mapping that was previously duplicated across
/// CryptoModule, KeyManagementService, TransactionService, WalletGrpcService, WalletEndpoints,
/// and ValidationEngine. All callers should use this class instead of inline switch expressions.
/// </para>
/// <para>
/// The name matching is case-insensitive and supports common aliases for each algorithm
/// (e.g., "NIST-P256", "P-256", "P256", and "ECDSA-P256" all map to <see cref="WalletNetworks.NISTP256"/>).
/// </para>
/// </remarks>
public static class AlgorithmMapper
{
    /// <summary>
    /// Parses an algorithm name string to its corresponding <see cref="WalletNetworks"/> enum value.
    /// </summary>
    /// <param name="algorithmName">
    /// The algorithm name (case-insensitive). Supports canonical names and common aliases:
    /// <list type="bullet">
    ///   <item>"ED25519"</item>
    ///   <item>"NISTP256", "NIST-P256", "P-256", "P256", "ECDSA-P256"</item>
    ///   <item>"RSA4096", "RSA-4096", "RSA"</item>
    ///   <item>"ML-DSA-65", "MLDSA65"</item>
    ///   <item>"SLH-DSA-128S", "SLHDSA128S"</item>
    ///   <item>"SLH-DSA-192S", "SLHDSA192S"</item>
    ///   <item>"ML-KEM-768", "MLKEM768"</item>
    /// </list>
    /// </param>
    /// <returns>The matching <see cref="WalletNetworks"/> value.</returns>
    /// <exception cref="ArgumentException">Thrown when the algorithm name is not recognized.</exception>
    public static WalletNetworks ParseAlgorithm(string algorithmName)
    {
        return TryParseAlgorithm(algorithmName, out var network)
            ? network
            : throw new ArgumentException($"Unsupported algorithm: {algorithmName}", nameof(algorithmName));
    }

    /// <summary>
    /// Attempts to parse an algorithm name string to its corresponding <see cref="WalletNetworks"/> enum value.
    /// </summary>
    /// <param name="algorithmName">The algorithm name (case-insensitive).</param>
    /// <param name="network">
    /// When this method returns <c>true</c>, contains the matching <see cref="WalletNetworks"/> value.
    /// When this method returns <c>false</c>, contains <see cref="WalletNetworks.ED25519"/> (default).
    /// </param>
    /// <returns><c>true</c> if the algorithm name was recognized; otherwise <c>false</c>.</returns>
    public static bool TryParseAlgorithm(string? algorithmName, out WalletNetworks network)
    {
        var result = algorithmName?.ToUpperInvariant() switch
        {
            "ED25519" => WalletNetworks.ED25519,
            "NISTP256" or "NIST-P256" or "P-256" or "P256" or "ECDSA-P256" => WalletNetworks.NISTP256,
            "RSA" or "RSA4096" or "RSA-4096" => WalletNetworks.RSA4096,
            "ML-DSA-65" or "MLDSA65" => WalletNetworks.ML_DSA_65,
            "SLH-DSA-128S" or "SLHDSA128S" => WalletNetworks.SLH_DSA_128s,
            "SLH-DSA-192S" or "SLHDSA192S" => WalletNetworks.SLH_DSA_192s,
            "ML-KEM-768" or "MLKEM768" => WalletNetworks.ML_KEM_768,
            _ => (WalletNetworks?)null
        };

        if (result.HasValue)
        {
            network = result.Value;
            return true;
        }

        network = default;
        return false;
    }

    /// <summary>
    /// Converts a <see cref="WalletNetworks"/> enum value to its canonical algorithm name string.
    /// </summary>
    /// <param name="network">The wallet network enum value.</param>
    /// <returns>The canonical algorithm name string (e.g., "ED25519", "ML-DSA-65").</returns>
    public static string ToAlgorithmName(WalletNetworks network) => network switch
    {
        WalletNetworks.ED25519 => "ED25519",
        WalletNetworks.NISTP256 => "NISTP256",
        WalletNetworks.RSA4096 => "RSA4096",
        WalletNetworks.ML_DSA_65 => "ML-DSA-65",
        WalletNetworks.SLH_DSA_128s => "SLH-DSA-128s",
        WalletNetworks.SLH_DSA_192s => "SLH-DSA-192s",
        WalletNetworks.ML_KEM_768 => "ML-KEM-768",
        _ => network.ToString()
    };
}
