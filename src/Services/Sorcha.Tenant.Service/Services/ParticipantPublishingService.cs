// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Publishes participant identity records as transactions on a register.
/// </summary>
public class ParticipantPublishingService : IParticipantPublishingService
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly ILogger<ParticipantPublishingService> _logger;

    /// <summary>
    /// Canonical JSON serializer options for deterministic payload hashing.
    /// MUST match the options used by Blueprint Service (TransactionBuilderServiceExtensions)
    /// and Validator Core (TransactionValidator) and Validator Service (ValidationEngine).
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ParticipantPublishingService(
        IRegisterServiceClient registerClient,
        IValidatorServiceClient validatorClient,
        IWalletServiceClient walletClient,
        ILogger<ParticipantPublishingService> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ParticipantPublishResult> PublishParticipantAsync(
        PublishParticipantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Publishing participant record to register {RegisterId} for org {OrgName}",
            request.RegisterId, request.OrganizationName);

        // 0. Is this role already published for this organisation? If so this is a CORRECTION, and
        //    it must supersede the existing record rather than sit beside it (#1668).
        //
        //    Creating a second Active record for one role did three things, each worse than the
        //    last: the register accumulated duplicates; resolution silently returned one of them
        //    (in practice the older, so the correction was ignored); and the new record chained
        //    from the SAME prevTxId as the original, so every later write against the original —
        //    revoke included — forked and was refused forever. A mis-bound role became permanent,
        //    with no way back short of a new register. Found by cold-start run #5.
        var existing = await FindPublishedRoleAsync(
            request.RegisterId, request.ParticipantName, request.OrganizationName, cancellationToken);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Participant role '{ParticipantName}' for org '{OrgName}' is already published on "
                + "register {RegisterId} as {ParticipantId} (v{Version}); superseding it rather than "
                + "publishing a duplicate.",
                request.ParticipantName, request.OrganizationName, request.RegisterId,
                existing.ParticipantId, existing.Version);

            return await UpdateParticipantAsync(new UpdatePublishedParticipantRequest
            {
                RegisterId = request.RegisterId,
                ParticipantId = existing.ParticipantId,
                ParticipantName = request.ParticipantName,
                OrganizationName = request.OrganizationName,
                Addresses = request.Addresses,
                SignerWalletAddress = request.SignerWalletAddress,
                Metadata = request.Metadata,
            }, cancellationToken);
        }

        // 1. Check wallet address uniqueness on the register (FR-010)
        await ValidateAddressUniquenessAsync(
            request.RegisterId, request.Addresses, excludeParticipantId: null, cancellationToken);

        // 2. Generate participant identity anchor (UUID)
        var participantId = Guid.NewGuid().ToString();

        // 3. Build the participant record payload
        var record = new ParticipantRecord
        {
            ParticipantId = participantId,
            OrganizationName = request.OrganizationName,
            ParticipantName = request.ParticipantName,
            Status = ParticipantRecordStatus.Active,
            Version = 1,
            Addresses = request.Addresses.Select(a => new ParticipantAddress
            {
                WalletAddress = a.WalletAddress,
                PublicKey = a.PublicKey,
                Algorithm = a.Algorithm,
                Primary = a.Primary
            }).ToList(),
            Metadata = request.Metadata
        };

        // 4. Fetch latest Control TX for PrevTxId chain (first publish chains from Control TX)
        var prevTxId = await GetLatestControlTxIdAsync(request.RegisterId, cancellationToken);

        // 5. Submit via shared logic
        return await SubmitParticipantRecord(
            record, request.RegisterId, request.SignerWalletAddress,
            prevTxId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ParticipantPublishResult> UpdateParticipantAsync(
        UpdatePublishedParticipantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Updating participant {ParticipantId} on register {RegisterId}",
            request.ParticipantId, request.RegisterId);

        // 1. Fetch current participant to get version and validate
        var current = await _registerClient.GetPublishedParticipantByIdAsync(
            request.RegisterId, request.ParticipantId, cancellationToken);

        if (current == null)
        {
            throw new KeyNotFoundException(
                $"Participant '{request.ParticipantId}' not found on register '{request.RegisterId}'");
        }

        var newVersion = current.Version + 1;

        // 1b. Check wallet address uniqueness (exclude current participant's own addresses)
        await ValidateAddressUniquenessAsync(
            request.RegisterId, request.Addresses, excludeParticipantId: request.ParticipantId, cancellationToken);

        // 2. Parse status (default to current if not specified)
        var status = ParticipantRecordStatus.Active;
        if (!string.IsNullOrEmpty(request.Status) &&
            Enum.TryParse<ParticipantRecordStatus>(request.Status, ignoreCase: true, out var parsed))
        {
            status = parsed;
        }

        // 3. Build updated record
        var record = new ParticipantRecord
        {
            ParticipantId = request.ParticipantId,
            OrganizationName = request.OrganizationName,
            ParticipantName = request.ParticipantName,
            Status = status,
            Version = newVersion,
            Addresses = request.Addresses.Select(a => new ParticipantAddress
            {
                WalletAddress = a.WalletAddress,
                PublicKey = a.PublicKey,
                Algorithm = a.Algorithm,
                Primary = a.Primary
            }).ToList(),
            Metadata = request.Metadata
        };

        // 4. Submit the updated record. It chains from the latest Control TX, exactly like a first
        //    publish — NOT from this participant's previous version. See GetLatestControlTxIdAsync.
        return await SubmitParticipantRecord(
            record, request.RegisterId, request.SignerWalletAddress,
            prevTxId: await GetLatestControlTxIdAsync(request.RegisterId, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ParticipantPublishResult> RevokeParticipantAsync(
        RevokeParticipantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Revoking participant {ParticipantId} on register {RegisterId}",
            request.ParticipantId, request.RegisterId);

        // 1. Fetch current participant
        var current = await _registerClient.GetPublishedParticipantByIdAsync(
            request.RegisterId, request.ParticipantId, cancellationToken);

        if (current == null)
        {
            throw new KeyNotFoundException(
                $"Participant '{request.ParticipantId}' not found on register '{request.RegisterId}'");
        }

        var newVersion = current.Version + 1;

        // 2. Build revocation record (preserve current data, set status to Revoked)
        var record = new ParticipantRecord
        {
            ParticipantId = request.ParticipantId,
            OrganizationName = current.OrganizationName,
            ParticipantName = current.ParticipantName,
            Status = ParticipantRecordStatus.Revoked,
            Version = newVersion,
            Addresses = current.Addresses.Select(a => new ParticipantAddress
            {
                WalletAddress = a.WalletAddress,
                PublicKey = a.PublicKey,
                Algorithm = a.Algorithm,
                Primary = a.Primary
            }).ToList()
        };

        // 3. Submit, chaining from the latest Control TX (see GetLatestControlTxIdAsync)
        return await SubmitParticipantRecord(
            record, request.RegisterId, request.SignerWalletAddress,
            prevTxId: await GetLatestControlTxIdAsync(request.RegisterId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Validates that none of the proposed wallet addresses are already claimed by another
    /// active participant on the same register. Throws InvalidOperationException (409) on conflict.
    /// </summary>
    /// <summary>
    /// Finds the live published record for a role on a register, or null if the role is unpublished.
    /// </summary>
    /// <remarks>
    /// A Revoked record is deliberately treated as absent: revocation is how a role is retired, so
    /// re-publishing afterwards is a fresh binding and should start a new record, not resurrect the
    /// retired one. A lookup failure also returns null — publishing a first record must not be
    /// blocked because the register could not be consulted, and the address-uniqueness check below
    /// still guards the case that matters.
    /// </remarks>
    private async Task<Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?> FindPublishedRoleAsync(
        string registerId, string participantName, string organizationName, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _registerClient.ResolveParticipantAsync(
                registerId, participantName, organizationName, cancellationToken);

            if (existing is null ||
                string.Equals(existing.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not check whether role '{ParticipantName}' is already published on register "
                + "{RegisterId}; treating it as unpublished.",
                participantName, registerId);
            return null;
        }
    }

    private async Task ValidateAddressUniquenessAsync(
        string registerId,
        IReadOnlyList<ParticipantAddressRequest> addresses,
        string? excludeParticipantId,
        CancellationToken cancellationToken)
    {
        foreach (var addr in addresses)
        {
            var existing = await _registerClient.GetPublishedParticipantByAddressAsync(
                registerId, addr.WalletAddress, cancellationToken);

            if (existing != null &&
                !string.Equals(existing.Status, "Revoked", StringComparison.OrdinalIgnoreCase) &&
                (excludeParticipantId == null ||
                 !string.Equals(existing.ParticipantId, excludeParticipantId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Wallet address '{addr.WalletAddress}' is already claimed by participant '{existing.ParticipantId}' on register '{registerId}'");
            }
        }
    }

    /// <summary>
    /// Shared submission logic for publish, update, and revoke operations.
    /// </summary>
    private async Task<ParticipantPublishResult> SubmitParticipantRecord(
        ParticipantRecord record,
        string registerId,
        string signerWalletAddress,
        string? prevTxId,
        CancellationToken cancellationToken)
    {
        // Serialize to canonical JSON
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(record, CanonicalJsonOptions);
        var payloadHash = ComputePayloadHash(payloadBytes);
        var txId = ComputeTxId(registerId, record.ParticipantId, record.Version);

        // Sign
        var signingData = Encoding.UTF8.GetBytes($"{txId}:{payloadHash}");
        var signResult = await _walletClient.SignTransactionAsync(
            signerWalletAddress, signingData, derivationPath: null,
            isPreHashed: false, cancellationToken: cancellationToken);

        // Build and submit
        var payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);

        // Fetch next sequence number for replay protection (SEC-AUDIT 4.2)
        var nextSeqNum = await _validatorClient.GetNextSequenceNumberAsync(
            registerId, signerWalletAddress, cancellationToken);

        var submission = new TransactionSubmission
        {
            TransactionId = txId,
            RegisterId = registerId,
            BlueprintId = null,
            ActionId = null,
            Payload = payload,
            PayloadHash = payloadHash,
            SequenceNumber = nextSeqNum,
            Signatures =
            [
                new SignatureInfo
                {
                    PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(signResult.Signature),
                    Algorithm = signResult.Algorithm,
                    SignedBy = signerWalletAddress
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = prevTxId,
            Metadata = new Dictionary<string, string>
            {
                ["Type"] = "Participant",
                ["participantId"] = record.ParticipantId,
                ["version"] = record.Version.ToString()
            }
        };

        _logger.LogDebug("Submitting participant TX {TxId} to validator (v{Version}, seq={SeqNum})",
            txId, record.Version, nextSeqNum);

        var result = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "Participant TX {TxId} rejected by validator: {Error} ({Code})",
                txId, result.ErrorMessage, result.ErrorCode);
            throw new InvalidOperationException(
                $"Participant record submission rejected: {result.ErrorMessage}");
        }

        _logger.LogInformation(
            "Participant record v{Version} published: TxId={TxId}, ParticipantId={ParticipantId}",
            record.Version, txId, record.ParticipantId);

        return new ParticipantPublishResult
        {
            TransactionId = txId,
            ParticipantId = record.ParticipantId,
            RegisterId = registerId,
            Version = record.Version,
            Status = "submitted",
            Message = $"Participant record v{record.Version} submitted to validation pipeline"
        };
    }

    /// <summary>
    /// Computes a deterministic TxId: SHA256("participant-publish-{registerId}-{participantId}-v{version}")
    /// </summary>
    internal static string ComputeTxId(string registerId, string participantId, int version)
    {
        var input = $"participant-publish-{registerId}-{participantId}-v{version}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes SHA-256 hash of payload bytes, returned as lowercase hex.
    /// </summary>
    internal static string ComputePayloadHash(byte[] payloadBytes)
    {
        var hashBytes = SHA256.HashData(payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// The predecessor for EVERY participant transaction — publish, update and revoke alike.
    /// </summary>
    /// <remarks>
    /// The validator's fork check allows one successor per predecessor, EXCEPT for a Control (or
    /// BlueprintPublish) predecessor, which may have many. Chaining every participant record from
    /// the latest Control TX is therefore always valid, however many participants are published.
    ///
    /// Anything else forks. Updates used to chain from the participant's own previous version, and
    /// first publishes used to chain from whatever transaction was newest on the register (the
    /// Control filter was never applied — see <c>GetControlTransactionsAsync</c>). Together they
    /// built a single linear chain through unrelated participants, so updating or revoking any
    /// participant but the last one picked a predecessor that already had a successor: the
    /// validator refused it with VAL_CHAIN_FORK after the API had already reported success.
    ///
    /// Version order does not need the chain — the register's participant index orders records
    /// by the payload's <c>Version</c>. Returning a non-Control transaction here is refused rather
    /// than trusted, because the wrong answer does not fail until a later write collides with it.
    /// </remarks>
    private async Task<string?> GetLatestControlTxIdAsync(
        string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var controlTxs = await _registerClient.GetControlTransactionsAsync(
                registerId, page: 1, pageSize: 1, cancellationToken);

            var latest = controlTxs.Transactions.FirstOrDefault();
            if (latest is not null && latest.MetaData?.TransactionType != TransactionType.Control)
            {
                // Chaining from null is fork-safe (the validator's chain checks only run when a
                // predecessor is named); chaining from this transaction is not.
                _logger.LogWarning(
                    "Register {RegisterId} returned {TxId} (type {Type}) as its latest Control TX; "
                    + "refusing to chain a participant record from it, chaining from null instead",
                    registerId, latest.TxId, latest.MetaData?.TransactionType);
                return null;
            }

            if (latest is not null)
            {
                var latestTxId = latest.TxId;
                _logger.LogDebug("Chaining from latest Control TX {TxId} on register {RegisterId}",
                    latestTxId, registerId);
                return latestTxId;
            }

            _logger.LogDebug("No Control TX found on register {RegisterId}, chaining from null", registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch Control TX for register {RegisterId}, chaining from null", registerId);
            return null;
        }
    }
}
