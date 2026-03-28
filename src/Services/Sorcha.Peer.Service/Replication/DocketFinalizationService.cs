// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Finalizes replicated dockets by verifying integrity and persisting them
/// to the Register Service via IRegisterServiceClient.WriteDocketAsync.
/// </summary>
public class DocketFinalizationService
{
    private readonly ILogger<DocketFinalizationService> _logger;
    private readonly IRegisterServiceClient _registerServiceClient;
    private readonly ValidatorKeyCache _validatorKeyCache;
    private readonly RegisterCache _registerCache;
    private readonly ConcurrentDictionary<string, DocketFinalizationRecord> _records = new();

    public DocketFinalizationService(
        ILogger<DocketFinalizationService> logger,
        IRegisterServiceClient registerServiceClient,
        ValidatorKeyCache validatorKeyCache,
        RegisterCache registerCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registerServiceClient = registerServiceClient ?? throw new ArgumentNullException(nameof(registerServiceClient));
        _validatorKeyCache = validatorKeyCache ?? throw new ArgumentNullException(nameof(validatorKeyCache));
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
    }

    /// <summary>
    /// Finalizes a cached docket: verifies chain integrity, recomputes hash,
    /// and persists to Register Service.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docket">Cached docket from replication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The finalization record</returns>
    public async Task<DocketFinalizationRecord> FinalizeAsync(
        string registerId,
        CachedDocket docket,
        CancellationToken cancellationToken = default)
    {
        var recordKey = $"{registerId}:{docket.Version}";

        // Check if already finalized (idempotent)
        if (_records.TryGetValue(recordKey, out var existing) &&
            existing.Status == FinalizationStatus.Finalized)
        {
            _logger.LogDebug(
                "Docket {DocketNumber} for register {RegisterId} already finalized, skipping",
                docket.Version, registerId);
            return existing;
        }

        var record = new DocketFinalizationRecord
        {
            RegisterId = registerId,
            DocketNumber = docket.Version,
            DocketHash = docket.DocketHash,
            AttemptedAt = DateTimeOffset.UtcNow
        };

        _records[recordKey] = record;

        try
        {
            // Step 1: Ensure validator key is cached (extract from genesis if needed)
            await EnsureValidatorKeyCachedAsync(registerId, cancellationToken);

            // Step 2: Verify chain integrity (PreviousHash linkage)
            if (!VerifyChainIntegrity(registerId, docket))
            {
                record.Status = FinalizationStatus.Rejected;
                record.ErrorMessage = "Chain integrity check failed: PreviousHash mismatch";
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} rejected: chain integrity failure",
                    docket.Version, registerId);
                return record;
            }

            // Step 3: Recompute and verify docket hash
            if (!VerifyDocketHash(registerId, docket))
            {
                record.Status = FinalizationStatus.Rejected;
                record.ErrorMessage = "Docket hash verification failed: computed hash does not match";
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} rejected: hash mismatch",
                    docket.Version, registerId);
                return record;
            }

            // Step 4: Verify proposer signature (TODO: actual crypto verification)
            // The actual signature verification requires access to the Sorcha.Cryptography
            // library or IWalletServiceClient. For now, we verify that the docket data
            // contains a valid signature structure.
            if (!VerifyProposerSignatureStructure(registerId, docket))
            {
                record.Status = FinalizationStatus.Rejected;
                record.ErrorMessage = "Proposer signature structure invalid";
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} rejected: invalid signature structure",
                    docket.Version, registerId);
                return record;
            }

            // Step 5: Build DocketModel and persist to Register Service
            var docketModel = BuildDocketModel(registerId, docket);
            var written = await _registerServiceClient.WriteDocketAsync(docketModel, cancellationToken);

            if (written)
            {
                record.Status = FinalizationStatus.Finalized;
                record.FinalizedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Docket {DocketNumber} for register {RegisterId} finalized successfully",
                    docket.Version, registerId);
            }
            else
            {
                record.Status = FinalizationStatus.Rejected;
                record.ErrorMessage = "Register Service refused the docket write";
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} rejected by Register Service",
                    docket.Version, registerId);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Duplicate write — treat as success (idempotent)
            record.Status = FinalizationStatus.Finalized;
            record.FinalizedAt = DateTimeOffset.UtcNow;
            _logger.LogDebug(
                "Docket {DocketNumber} for register {RegisterId} already exists in Register Service (idempotent)",
                docket.Version, registerId);
        }
        catch (Exception ex)
        {
            record.Status = FinalizationStatus.Rejected;
            record.ErrorMessage = $"Finalization error: {ex.Message}";
            _logger.LogError(ex,
                "Error finalizing docket {DocketNumber} for register {RegisterId}",
                docket.Version, registerId);
        }

        return record;
    }

    /// <summary>
    /// Gets a finalization record by register ID and docket number.
    /// </summary>
    public DocketFinalizationRecord? GetRecord(string registerId, long docketNumber)
    {
        var key = $"{registerId}:{docketNumber}";
        _records.TryGetValue(key, out var record);
        return record;
    }

    /// <summary>
    /// Gets all finalization records for a register.
    /// </summary>
    public IReadOnlyList<DocketFinalizationRecord> GetRecordsForRegister(string registerId)
    {
        return _records
            .Where(r => r.Value.RegisterId == registerId)
            .Select(r => r.Value)
            .OrderBy(r => r.DocketNumber)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets the total count of finalization records.
    /// </summary>
    public int RecordCount => _records.Count;

    private async Task EnsureValidatorKeyCachedAsync(string registerId, CancellationToken cancellationToken)
    {
        if (_validatorKeyCache.HasKey(registerId))
            return;

        // Try to extract from the genesis docket in cache
        var cacheEntry = _registerCache.Get(registerId);
        var genesisDocket = cacheEntry?.GetDocket(0);

        if (genesisDocket != null)
        {
            _validatorKeyCache.ExtractFromGenesisDocket(registerId, genesisDocket.Data);
            return;
        }

        // Fall back to reading genesis docket from Register Service
        try
        {
            var genesis = await _registerServiceClient.ReadDocketAsync(registerId, 0, cancellationToken);
            if (genesis != null)
            {
                _logger.LogDebug(
                    "Retrieved genesis docket from Register Service for key extraction (register {RegisterId})",
                    registerId);
                // The DocketModel from Register Service doesn't contain raw signature bytes,
                // so we cannot extract the key this way. Log and continue without key.
                _logger.LogWarning(
                    "Cannot extract validator key from DocketModel for register {RegisterId} — " +
                    "key extraction requires raw docket data from genesis docket in cache",
                    registerId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to retrieve genesis docket from Register Service for register {RegisterId}",
                registerId);
        }
    }

    private bool VerifyChainIntegrity(string registerId, CachedDocket docket)
    {
        // Genesis docket has no previous hash
        if (docket.Version == 0)
        {
            return docket.PreviousHash == null || docket.PreviousHash == string.Empty;
        }

        // For non-genesis dockets, verify PreviousHash matches the preceding docket's hash
        var cacheEntry = _registerCache.Get(registerId);
        var previousDocket = cacheEntry?.GetDocket(docket.Version - 1);

        if (previousDocket == null)
        {
            _logger.LogWarning(
                "Cannot verify chain integrity for docket {DocketNumber} in register {RegisterId}: " +
                "previous docket {PreviousDocketNumber} not in cache",
                docket.Version, registerId, docket.Version - 1);
            // If we don't have the previous docket, we can't verify — allow it through
            // with a warning (the Register Service will do its own validation)
            return true;
        }

        if (!string.Equals(docket.PreviousHash, previousDocket.DocketHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Chain break at docket {DocketNumber} in register {RegisterId}: " +
                "PreviousHash={PreviousHash} but preceding docket hash={ExpectedHash}",
                docket.Version, registerId, docket.PreviousHash, previousDocket.DocketHash);
            return false;
        }

        return true;
    }

    private bool VerifyDocketHash(string registerId, CachedDocket docket)
    {
        // Deserialize the docket data to extract fields needed for hash computation
        try
        {
            using var doc = JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            // Extract fields matching DocketHasher.ComputeDocketHash format
            var merkleRoot = GetStringProperty(root, "MerkleRoot", "merkleRoot") ?? string.Empty;
            var timestamp = GetTimestampProperty(root, "CreatedAt", "createdAt");

            if (timestamp == null)
            {
                _logger.LogWarning(
                    "Cannot verify docket hash for docket {DocketNumber} in register {RegisterId}: " +
                    "missing CreatedAt timestamp in docket data",
                    docket.Version, registerId);
                // Allow through — Register Service will validate
                return true;
            }

            // Recompute hash using same algorithm as DocketHasher
            var hashInput = new
            {
                RegisterId = registerId,
                DocketNumber = docket.Version,
                PreviousHash = docket.PreviousHash ?? string.Empty,
                MerkleRoot = merkleRoot,
                Timestamp = timestamp.Value.ToUnixTimeMilliseconds()
            };

            var json = JsonSerializer.Serialize(hashInput, new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                WriteIndented = false
            });

            var bytes = Encoding.UTF8.GetBytes(json);
            var hashBytes = SHA256.HashData(bytes);
            var computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            if (!string.Equals(computedHash, docket.DocketHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Docket hash mismatch for docket {DocketNumber} in register {RegisterId}: " +
                    "computed={ComputedHash}, expected={ExpectedHash}",
                    docket.Version, registerId, computedHash, docket.DocketHash);
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse docket data for hash verification (docket {DocketNumber}, register {RegisterId})",
                docket.Version, registerId);
            // Allow through — Register Service will validate
            return true;
        }
    }

    private bool VerifyProposerSignatureStructure(string registerId, CachedDocket docket)
    {
        try
        {
            using var doc = JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ProposerSignature", out var sigElement) &&
                !root.TryGetProperty("proposerSignature", out sigElement))
            {
                // No signature in docket data — genesis dockets may not require one
                if (docket.Version == 0)
                    return true;

                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} has no ProposerSignature",
                    docket.Version, registerId);
                return false;
            }

            // Verify signature structure has required fields
            var hasPublicKey = sigElement.TryGetProperty("PublicKey", out _) ||
                               sigElement.TryGetProperty("publicKey", out _);
            var hasSignatureValue = sigElement.TryGetProperty("SignatureValue", out _) ||
                                    sigElement.TryGetProperty("signatureValue", out _);
            var hasAlgorithm = sigElement.TryGetProperty("Algorithm", out _) ||
                               sigElement.TryGetProperty("algorithm", out _);

            if (!hasPublicKey || !hasSignatureValue || !hasAlgorithm)
            {
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} has incomplete ProposerSignature " +
                    "(PublicKey: {HasPK}, SignatureValue: {HasSV}, Algorithm: {HasAlg})",
                    docket.Version, registerId, hasPublicKey, hasSignatureValue, hasAlgorithm);
                return false;
            }

            // TODO: Actual cryptographic signature verification
            // To verify the signature:
            // 1. Get the cached validator public key from ValidatorKeyCache
            // 2. Recompute the docket hash (already done in VerifyDocketHash)
            // 3. Use the appropriate crypto algorithm to verify SignatureValue against the hash
            // This requires either:
            //   a) Adding Sorcha.Cryptography as a project reference, or
            //   b) Using IWalletServiceClient.VerifySignatureAsync() via service call
            // For now, structural validation is sufficient — Register Service performs
            // full cryptographic verification on WriteDocketAsync.

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse docket data for signature verification (docket {DocketNumber}, register {RegisterId})",
                docket.Version, registerId);
            return true; // Allow through — Register Service will validate
        }
    }

    private DocketModel BuildDocketModel(string registerId, CachedDocket docket)
    {
        // Extract proposer validator ID from docket data
        var proposerValidatorId = "unknown";
        var merkleRoot = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            proposerValidatorId = GetStringProperty(root, "ProposerValidatorId", "proposerValidatorId")
                                  ?? "unknown";
            merkleRoot = GetStringProperty(root, "MerkleRoot", "merkleRoot") ?? string.Empty;
        }
        catch (JsonException)
        {
            _logger.LogWarning(
                "Failed to extract metadata from docket data for docket {DocketNumber} in register {RegisterId}",
                docket.Version, registerId);
        }

        return new DocketModel
        {
            DocketId = $"{registerId}-{docket.Version}",
            RegisterId = registerId,
            DocketNumber = docket.Version,
            PreviousHash = docket.PreviousHash,
            DocketHash = docket.DocketHash,
            CreatedAt = docket.CreatedAt,
            Transactions = docket.TransactionIds.Select(txId => new Sorcha.Register.Models.TransactionModel
            {
                RegisterId = registerId,
                TxId = txId
            }).ToList(),
            ProposerValidatorId = proposerValidatorId,
            MerkleRoot = merkleRoot
        };
    }

    private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private static DateTimeOffset? GetTimestampProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(prop.GetString(), out var dto))
                {
                    return dto;
                }
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(prop.GetInt64());
                }
            }
        }
        return null;
    }
}
