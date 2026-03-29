// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Thread-safe cache of validator public keys per register, extracted from genesis dockets.
/// Used by DocketFinalizationService to verify proposer signatures without calling Wallet Service.
/// </summary>
public class ValidatorKeyCache
{
    private readonly ILogger<ValidatorKeyCache> _logger;
    private readonly ConcurrentDictionary<string, ValidatorKeyEntry> _keys = new();

    public ValidatorKeyCache(ILogger<ValidatorKeyCache> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Tries to get the cached validator key for a register.
    /// </summary>
    public bool TryGetKey(string registerId, out ValidatorKeyEntry? entry)
    {
        return _keys.TryGetValue(registerId, out entry);
    }

    /// <summary>
    /// Caches a validator key entry for a register.
    /// </summary>
    public void CacheKey(ValidatorKeyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _keys[entry.RegisterId] = entry;
        _logger.LogDebug(
            "Cached validator key for register {RegisterId} (algorithm: {Algorithm}, source: {Source})",
            entry.RegisterId, entry.Algorithm, entry.ResolvedFrom);
    }

    /// <summary>
    /// Attempts to extract the validator public key from a genesis docket's serialized data.
    /// The genesis docket (DocketNumber 0) contains the proposer signature with the validator's public key.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="genesisDocketData">Serialized genesis docket bytes</param>
    /// <returns>True if key was successfully extracted and cached</returns>
    public bool ExtractFromGenesisDocket(string registerId, byte[] genesisDocketData)
    {
        try
        {
            using var doc = JsonDocument.Parse(genesisDocketData);
            var root = doc.RootElement;

            // Try to extract ProposerSignature from the docket data
            if (!root.TryGetProperty("ProposerSignature", out var sigElement) &&
                !root.TryGetProperty("proposerSignature", out sigElement))
            {
                _logger.LogWarning(
                    "Genesis docket for register {RegisterId} does not contain ProposerSignature",
                    registerId);
                return false;
            }

            byte[]? publicKey = null;
            string? algorithm = null;

            if (sigElement.TryGetProperty("PublicKey", out var pkElement) ||
                sigElement.TryGetProperty("publicKey", out pkElement))
            {
                publicKey = pkElement.GetBytesFromBase64();
            }

            if (sigElement.TryGetProperty("Algorithm", out var algElement) ||
                sigElement.TryGetProperty("algorithm", out algElement))
            {
                algorithm = algElement.GetString();
            }

            if (publicKey == null || publicKey.Length == 0 || string.IsNullOrEmpty(algorithm))
            {
                _logger.LogWarning(
                    "Genesis docket for register {RegisterId} has incomplete ProposerSignature (key: {HasKey}, algorithm: {Algorithm})",
                    registerId, publicKey != null, algorithm);
                return false;
            }

            var entry = new ValidatorKeyEntry
            {
                RegisterId = registerId,
                PublicKey = publicKey,
                Algorithm = algorithm,
                ResolvedFrom = "genesis-docket",
                ResolvedAt = DateTimeOffset.UtcNow
            };

            CacheKey(entry);

            _logger.LogInformation(
                "Extracted validator key from genesis docket for register {RegisterId} ({Algorithm}, {KeyLength} bytes)",
                registerId, algorithm, publicKey.Length);

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse genesis docket data for register {RegisterId}", registerId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error extracting validator key from genesis docket for register {RegisterId}",
                registerId);
            return false;
        }
    }

    /// <summary>
    /// Returns whether a key is cached for the given register.
    /// </summary>
    public bool HasKey(string registerId)
    {
        return _keys.ContainsKey(registerId);
    }

    /// <summary>
    /// Gets the number of cached validator keys.
    /// </summary>
    public int Count => _keys.Count;
}

/// <summary>
/// Cached validator public key entry for a register.
/// </summary>
public class ValidatorKeyEntry
{
    /// <summary>
    /// Register this key belongs to.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Validator's public key bytes.
    /// </summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>
    /// Signing algorithm (e.g., "ED25519", "NISTP256", "RSA4096").
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// How the key was resolved (e.g., "genesis-docket", "wallet-service").
    /// </summary>
    public required string ResolvedFrom { get; init; }

    /// <summary>
    /// When the key was resolved.
    /// </summary>
    public required DateTimeOffset ResolvedAt { get; init; }
}
