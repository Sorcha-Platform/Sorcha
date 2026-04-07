// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ValidatorKeyCache _validatorKeyCache;
    private readonly RegisterCache _registerCache;
    private readonly ICryptoModule _cryptoModule;
    private readonly DocketHasher _docketHasher;
    private readonly ConcurrentDictionary<string, DocketFinalizationRecord> _records = new();

    public DocketFinalizationService(
        ILogger<DocketFinalizationService> logger,
        IServiceScopeFactory scopeFactory,
        ValidatorKeyCache validatorKeyCache,
        RegisterCache registerCache,
        ICryptoModule cryptoModule,
        DocketHasher docketHasher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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

            // Step 5: Build DocketModel and persist to Register Service (scoped client)
            var docketModel = BuildDocketModel(registerId, docket);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
            var written = await registerClient.WriteDocketAsync(docketModel, cancellationToken);

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

        // Strategy 1: Extract validator roster from genesis control transaction payload in cache.
        // The genesis docket's transactions contain a Control transaction whose payload
        // is a Base64Url-encoded RegisterControlRecord with a validators field.
        var cacheEntry = _registerCache.Get(registerId);
        var genesisDocket = cacheEntry?.GetDocket(0);

        if (genesisDocket != null)
        {
            if (TryExtractValidatorRosterFromDocket(registerId, genesisDocket))
                return;

            // Fall back to legacy ProposerSignature extraction for pre-086 registers
            _validatorKeyCache.ExtractFromGenesisDocket(registerId, genesisDocket.Data);
            if (_validatorKeyCache.HasKey(registerId))
                return;
        }

        // Strategy 2: Read genesis transaction from Register Service (for locally-owned registers).
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

            // Get the genesis transaction directly — it contains the control record payload
            var genesis = await registerClient.ReadDocketAsync(registerId, 0, cancellationToken);
            if (genesis?.Transactions.Count > 0)
            {
                // Find the Control transaction in the genesis docket (don't assume index 0)
                foreach (var txStub in genesis.Transactions)
                {
                    var tx = await registerClient.GetTransactionAsync(
                        registerId, txStub.TxId, cancellationToken);

                    // Only process Control transactions (MetaData.TransactionType == 0)
                    if (tx?.MetaData?.TransactionType != 0) continue;
                    if (tx.Payloads?.Length == 0) continue;

                    var payloadData = tx.Payloads[0].Data;
                    if (string.IsNullOrEmpty(payloadData)) continue;

                    var controlRecordBytes = DecodeBase64Url(payloadData);
                    if (_validatorKeyCache.ExtractFromControlRecord(registerId, controlRecordBytes))
                    {
                        _logger.LogInformation(
                            "Extracted validator roster from Register Service genesis transaction for register {RegisterId}",
                            registerId);
                        return;
                    }
                }
            }

            _logger.LogWarning(
                "Could not extract validator roster from Register Service for register {RegisterId}",
                registerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to retrieve genesis transaction from Register Service for register {RegisterId}",
                registerId);
        }
    }

    /// <summary>
    /// Attempts to extract the validator roster from a cached genesis docket's transaction data.
    /// Parses the genesis transactions to find the Control transaction and its RegisterControlRecord payload.
    /// </summary>
    private bool TryExtractValidatorRosterFromDocket(string registerId, CachedDocket genesisDocket)
    {
        try
        {
            // The genesis docket data is serialized DocketModel JSON containing transactions.
            // Each transaction may have Base64Url-encoded payloads with RegisterControlRecord.
            using var doc = JsonDocument.Parse(genesisDocket.Data);
            var root = doc.RootElement;

            // Look for transactions array in the docket data
            JsonElement txArray;
            if (!root.TryGetProperty("Transactions", out txArray) &&
                !root.TryGetProperty("transactions", out txArray))
            {
                return false;
            }

            foreach (var tx in txArray.EnumerateArray())
            {
                // Only process Control transactions (TransactionType == 0)
                if ((tx.TryGetProperty("MetaData", out var meta) || tx.TryGetProperty("metaData", out meta))
                    && meta.ValueKind == JsonValueKind.Object)
                {
                    if (meta.TryGetProperty("TransactionType", out var txType) ||
                        meta.TryGetProperty("transactionType", out txType))
                    {
                        if (txType.ValueKind == JsonValueKind.Number && txType.GetInt32() != 0)
                            continue;
                    }
                }

                // Look for payloads
                JsonElement payloads;
                if (!tx.TryGetProperty("Payloads", out payloads) &&
                    !tx.TryGetProperty("payloads", out payloads))
                    continue;

                foreach (var payload in payloads.EnumerateArray())
                {
                    JsonElement dataElement;
                    if (!payload.TryGetProperty("Data", out dataElement) &&
                        !payload.TryGetProperty("data", out dataElement))
                        continue;

                    var payloadData = dataElement.GetString();
                    if (string.IsNullOrEmpty(payloadData)) continue;

                    try
                    {
                        var controlRecordBytes = DecodeBase64Url(payloadData);
                        if (_validatorKeyCache.ExtractFromControlRecord(registerId, controlRecordBytes))
                        {
                            _logger.LogInformation(
                                "Extracted validator roster from cached genesis docket for register {RegisterId}",
                                registerId);
                            return true;
                        }
                    }
                    catch (FormatException)
                    {
                        // Not valid Base64Url — try next payload
                    }
                }
            }

            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex,
                "Failed to parse genesis docket transactions for register {RegisterId}", registerId);
            return false;
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

            // If MerkleRoot is missing (not carried through DocketModel serialization),
            // we cannot recompute the hash. Skip verification — the Register Service
            // will validate when the docket is written via WriteDocketAsync.
            if (string.IsNullOrEmpty(merkleRoot))
            {
                _logger.LogDebug(
                    "Skipping hash verification for docket {DocketNumber} in register {RegisterId}: MerkleRoot not available in relay data",
                    docket.Version, registerId);
                return true;
            }

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

            // Verify the signer is in the authorized validator roster (FR-008)
            if (_validatorKeyCache.HasKey(registerId) &&
                !_validatorKeyCache.IsAuthorizedSigner(registerId, publicKeyBytes))
            {
                _logger.LogWarning(
                    "Docket {DocketNumber} for register {RegisterId}: proposer public key is not in the authorized validator roster",
                    docket.Version, registerId);
                return false;
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

    /// <summary>
    /// Decodes a Base64Url-encoded string to bytes.
    /// Handles the URL-safe alphabet (- instead of +, _ instead of /) and missing padding.
    /// </summary>
    private static byte[] DecodeBase64Url(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
