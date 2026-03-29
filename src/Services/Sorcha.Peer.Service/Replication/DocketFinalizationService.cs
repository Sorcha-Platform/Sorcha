// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
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
    private readonly ICryptoModule _cryptoModule;
    private readonly DocketHasher _docketHasher;
    private readonly ConcurrentDictionary<string, DocketFinalizationRecord> _records = new();

    public DocketFinalizationService(
        ILogger<DocketFinalizationService> logger,
        IRegisterServiceClient registerServiceClient,
        ValidatorKeyCache validatorKeyCache,
        RegisterCache registerCache,
        ICryptoModule cryptoModule,
        DocketHasher docketHasher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registerServiceClient = registerServiceClient ?? throw new ArgumentNullException(nameof(registerServiceClient));
        _validatorKeyCache = validatorKeyCache ?? throw new ArgumentNullException(nameof(validatorKeyCache));
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _docketHasher = docketHasher ?? throw new ArgumentNullException(nameof(docketHasher));
    }

    /// <summary>
    /// Removes finalized records older than the specified retention period.
    /// Called periodically to prevent unbounded memory growth.
    /// </summary>
    public int EvictOldRecords(TimeSpan retention)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;
        var keysToRemove = _records
            .Where(r => r.Value.Status == FinalizationStatus.Finalized && r.Value.FinalizedAt < cutoff)
            .Select(r => r.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _records.TryRemove(key, out _);

        return keysToRemove.Count;
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
        // Evict stale finalized records to prevent unbounded memory growth
        if (_records.Count > 1000)
            EvictOldRecords(TimeSpan.FromHours(24));

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

            // Fail if validator key could not be resolved (except for genesis dockets)
            if (!_validatorKeyCache.HasKey(registerId) && docket.Version != 0)
            {
                record.Status = FinalizationStatus.Rejected;
                record.ErrorMessage = "Cannot verify: validator key not available";
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} rejected: validator key not available",
                    docket.Version, registerId);
                return record;
            }

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

            // Step 4: Verify proposer signature cryptographically
            if (!await VerifyProposerSignatureAsync(registerId, docket, cancellationToken))
            {
                record.Status = FinalizationStatus.Rejected;
                record.ErrorMessage = "Proposer signature verification failed";
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} rejected: signature verification failed",
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

            // Use DocketHasher for deterministic hash verification
            var verified = _docketHasher.VerifyDocketHash(
                registerId,
                docket.Version,
                docket.PreviousHash,
                merkleRoot,
                timestamp.Value,
                docket.DocketHash);

            if (!verified)
            {
                _logger.LogWarning(
                    "Docket hash mismatch for docket {DocketNumber} in register {RegisterId}: " +
                    "expected={ExpectedHash}",
                    docket.Version, registerId, docket.DocketHash);
            }

            return verified;
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

    private async Task<bool> VerifyProposerSignatureAsync(
        string registerId,
        CachedDocket docket,
        CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ProposerSignature", out var sigElement) &&
                !root.TryGetProperty("proposerSignature", out sigElement))
            {
                // Genesis dockets (version 0) may not have a signature
                if (docket.Version == 0)
                    return true;

                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} has no ProposerSignature",
                    docket.Version, registerId);
                return false;
            }

            // Extract signature fields
            byte[]? publicKeyBytes = null;
            byte[]? signatureBytes = null;
            string? algorithm = null;

            if (sigElement.TryGetProperty("PublicKey", out var pkElement) ||
                sigElement.TryGetProperty("publicKey", out pkElement))
            {
                publicKeyBytes = pkElement.GetBytesFromBase64();
            }

            if (sigElement.TryGetProperty("SignatureValue", out var svElement) ||
                sigElement.TryGetProperty("signatureValue", out svElement))
            {
                signatureBytes = svElement.GetBytesFromBase64();
            }

            if (sigElement.TryGetProperty("Algorithm", out var algElement) ||
                sigElement.TryGetProperty("algorithm", out algElement))
            {
                algorithm = algElement.GetString();
            }

            if (publicKeyBytes == null || signatureBytes == null || string.IsNullOrEmpty(algorithm))
            {
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} has incomplete ProposerSignature " +
                    "(PublicKey: {HasPK}, SignatureValue: {HasSV}, Algorithm: {HasAlg})",
                    docket.Version, registerId,
                    publicKeyBytes != null, signatureBytes != null, algorithm);
                return false;
            }

            // Parse the algorithm to get the WalletNetworks byte
            if (!AlgorithmMapper.TryParseAlgorithm(algorithm, out var network))
            {
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId} has unsupported algorithm: {Algorithm}",
                    docket.Version, registerId, algorithm);
                return false;
            }

            // Use the cached validator key if available for public key cross-check
            if (_validatorKeyCache.TryGetKey(registerId, out var cachedKey) && cachedKey != null)
            {
                if (!publicKeyBytes.SequenceEqual(cachedKey.PublicKey))
                {
                    _logger.LogWarning(
                        "Docket {DocketNumber} for register {RegisterId}: proposer public key does not match cached validator key",
                        docket.Version, registerId);
                    return false;
                }
            }

            // Recompute the docket hash bytes for verification
            // DocketHash is a hex string; the Wallet Service signs Convert.FromHexString(hex) with isPreHashed,
            // so verification must use the same raw bytes.
            var hashBytes = Convert.FromHexString(docket.DocketHash);

            // Cryptographically verify the signature
            var status = await _cryptoModule.VerifyAsync(
                signatureBytes, hashBytes, (byte)network, publicKeyBytes, cancellationToken);

            if (status == CryptoStatus.Success)
            {
                return true;
            }

            _logger.LogWarning(
                "Docket {DocketNumber} for register {RegisterId}: cryptographic signature verification failed (status: {Status})",
                docket.Version, registerId, status);
            return false;
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
        // Extract proposer validator ID and other metadata from docket data
        var proposerValidatorId = "unknown";
        var merkleRoot = string.Empty;
        string? docketId = null;

        try
        {
            using var doc = JsonDocument.Parse(docket.Data);
            var root = doc.RootElement;

            proposerValidatorId = GetStringProperty(root, "ProposerValidatorId", "proposerValidatorId")
                                  ?? "unknown";
            merkleRoot = GetStringProperty(root, "MerkleRoot", "merkleRoot") ?? string.Empty;
            docketId = GetStringProperty(root, "DocketId", "docketId");
        }
        catch (JsonException)
        {
            _logger.LogWarning(
                "Failed to extract metadata from docket data for docket {DocketNumber} in register {RegisterId}",
                docket.Version, registerId);
        }

        // Use DocketId from the docket data if available, fall back to the DocketHash (unique and deterministic)
        docketId ??= docket.DocketHash;

        // Hydrate full TransactionModel objects from the register cache
        var cacheEntry = _registerCache.Get(registerId);
        var transactions = docket.TransactionIds.Select(txId =>
        {
            var cachedTx = cacheEntry?.GetTransaction(txId);
            if (cachedTx != null)
            {
                // Deserialize the full transaction from cached data
                try
                {
                    var txModel = JsonSerializer.Deserialize<Sorcha.Register.Models.TransactionModel>(cachedTx.Data);
                    if (txModel != null)
                    {
                        txModel.RegisterId = registerId;
                        txModel.TxId = txId;
                        return txModel;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to deserialize cached transaction {TxId} for register {RegisterId}",
                        txId, registerId);
                }
            }

            // Fall back to minimal transaction if not in cache or deserialization failed
            return new Sorcha.Register.Models.TransactionModel
            {
                RegisterId = registerId,
                TxId = txId
            };
        }).ToList();

        return new DocketModel
        {
            DocketId = docketId,
            RegisterId = registerId,
            DocketNumber = docket.Version,
            PreviousHash = docket.PreviousHash,
            DocketHash = docket.DocketHash,
            CreatedAt = docket.CreatedAt,
            Transactions = transactions,
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
