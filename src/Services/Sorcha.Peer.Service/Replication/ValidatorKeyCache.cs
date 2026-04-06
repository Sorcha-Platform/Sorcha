// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Thread-safe cache of authorized validator signing keys per register.
/// Extracts the validator roster from genesis control records and supports
/// multi-key verification for registers with multiple authorized validators.
/// </summary>
public class ValidatorKeyCache
{
    private readonly ILogger<ValidatorKeyCache> _logger;
    private readonly ConcurrentDictionary<string, ValidatorRosterCache> _rosters = new();

    public ValidatorKeyCache(ILogger<ValidatorKeyCache> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks whether a public key is an authorized signer for the given register.
    /// Returns true if the key matches any Active or Rotated entry in the cached roster.
    /// </summary>
    public bool IsAuthorizedSigner(string registerId, byte[] publicKey)
    {
        if (!_rosters.TryGetValue(registerId, out var roster))
            return false;

        return roster.AuthorizedKeys.Any(k =>
            k.PublicKey.AsSpan().SequenceEqual(publicKey));
    }

    /// <summary>
    /// Gets the cached roster for a register, or null if not cached.
    /// </summary>
    public ValidatorRosterCache? GetRoster(string registerId)
    {
        _rosters.TryGetValue(registerId, out var roster);
        return roster;
    }

    /// <summary>
    /// Extracts the validator roster from a genesis control record's payload.
    /// The payload is the decoded RegisterControlRecord JSON from the genesis transaction.
    /// </summary>
    public bool ExtractFromControlRecord(string registerId, byte[] controlRecordData)
    {
        try
        {
            var controlRecord = JsonSerializer.Deserialize<RegisterControlRecord>(controlRecordData);
            if (controlRecord?.Validators == null)
            {
                _logger.LogWarning(
                    "Genesis control record for register {RegisterId} does not contain a validators field",
                    registerId);
                return false;
            }

            return ExtractFromControlRecord(registerId, controlRecord);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize control record for register {RegisterId}", registerId);
            return false;
        }
    }

    /// <summary>
    /// Extracts the validator roster from a deserialized RegisterControlRecord.
    /// </summary>
    public bool ExtractFromControlRecord(string registerId, RegisterControlRecord controlRecord)
    {
        if (controlRecord.Validators == null || controlRecord.Validators.Validators.Count == 0)
        {
            _logger.LogWarning(
                "Control record for register {RegisterId} has no validator entries",
                registerId);
            return false;
        }

        var authorizedKeys = controlRecord.Validators.VerifiableValidators
            .Select(v =>
            {
                try
                {
                    return new ValidatorKeyEntry
                    {
                        RegisterId = registerId,
                        PublicKey = Convert.FromBase64String(v.PublicKey),
                        Algorithm = v.Algorithm.ToString(),
                        ResolvedFrom = "genesis-control-record",
                        ResolvedAt = DateTimeOffset.UtcNow
                    };
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex,
                        "Invalid Base64 public key for validator {ValidatorId} in register {RegisterId}",
                        v.ValidatorId, registerId);
                    return null;
                }
            })
            .Where(k => k != null)
            .Cast<ValidatorKeyEntry>()
            .ToList();

        if (authorizedKeys.Count == 0)
        {
            _logger.LogWarning(
                "No valid authorized keys extracted from control record for register {RegisterId}",
                registerId);
            return false;
        }

        var rosterCache = new ValidatorRosterCache
        {
            RegisterId = registerId,
            AuthorizedKeys = authorizedKeys,
            RequiredSignatures = controlRecord.Validators.RequiredSignatures,
            RosterVersion = controlRecord.Validators.Version,
            ResolvedFrom = "genesis-control-record",
            ResolvedAt = DateTimeOffset.UtcNow
        };

        _rosters[registerId] = rosterCache;

        _logger.LogInformation(
            "Cached validator roster for register {RegisterId}: {KeyCount} authorized keys, requiredSignatures={Required}, version={Version}",
            registerId, authorizedKeys.Count, rosterCache.RequiredSignatures, rosterCache.RosterVersion);

        return true;
    }

    /// <summary>
    /// Legacy extraction from genesis docket ProposerSignature for pre-086 registers.
    /// Falls back to single-key extraction when no control record roster is available.
    /// </summary>
    public bool ExtractFromGenesisDocket(string registerId, byte[] genesisDocketData)
    {
        try
        {
            using var doc = JsonDocument.Parse(genesisDocketData);
            var root = doc.RootElement;

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
                    "Genesis docket for register {RegisterId} has incomplete ProposerSignature",
                    registerId);
                return false;
            }

            var rosterCache = new ValidatorRosterCache
            {
                RegisterId = registerId,
                AuthorizedKeys =
                [
                    new ValidatorKeyEntry
                    {
                        RegisterId = registerId,
                        PublicKey = publicKey,
                        Algorithm = algorithm,
                        ResolvedFrom = "genesis-docket-legacy",
                        ResolvedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                RosterVersion = 0,
                ResolvedFrom = "genesis-docket-legacy",
                ResolvedAt = DateTimeOffset.UtcNow
            };

            _rosters[registerId] = rosterCache;

            _logger.LogInformation(
                "Extracted legacy validator key from genesis docket for register {RegisterId} ({Algorithm})",
                registerId, algorithm);

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse genesis docket data for register {RegisterId}", registerId);
            return false;
        }
    }

    /// <summary>
    /// Returns whether a roster is cached for the given register.
    /// </summary>
    public bool HasKey(string registerId) => _rosters.ContainsKey(registerId);

    /// <summary>
    /// Backward-compatible single-key lookup. Returns the first authorized key entry.
    /// </summary>
    public bool TryGetKey(string registerId, out ValidatorKeyEntry? entry)
    {
        if (_rosters.TryGetValue(registerId, out var roster) && roster.AuthorizedKeys.Count > 0)
        {
            entry = roster.AuthorizedKeys[0];
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Gets the number of cached register rosters.
    /// </summary>
    public int Count => _rosters.Count;
}

/// <summary>
/// Cached validator roster for a register.
/// </summary>
public class ValidatorRosterCache
{
    public required string RegisterId { get; init; }
    public required List<ValidatorKeyEntry> AuthorizedKeys { get; init; }
    public required int RequiredSignatures { get; init; }
    public int RosterVersion { get; init; }
    public required string ResolvedFrom { get; init; }
    public required DateTimeOffset ResolvedAt { get; init; }
}

/// <summary>
/// Cached validator public key entry for a register.
/// </summary>
public class ValidatorKeyEntry
{
    public required string RegisterId { get; init; }
    public required byte[] PublicKey { get; init; }
    public required string Algorithm { get; init; }
    public required string ResolvedFrom { get; init; }
    public required DateTimeOffset ResolvedAt { get; init; }
}
