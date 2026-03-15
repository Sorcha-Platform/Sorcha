// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Orchestrates the two-phase register creation workflow with genesis transactions
/// </summary>
public class RegisterCreationOrchestrator : IRegisterCreationOrchestrator
{
    private readonly ILogger<RegisterCreationOrchestrator> _logger;
    private readonly RegisterManager _registerManager;
    private readonly TransactionManager _transactionManager;
    private readonly IWalletServiceClient _walletClient;
    private readonly IHashProvider _hashProvider;
    private readonly ICryptoModule _cryptoModule;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly ISystemWalletSigningService _signingService;
    private readonly IPendingRegistrationStore _pendingStore;
    private readonly IPeerServiceClient _peerClient;

    private readonly TimeSpan _pendingExpirationTime = TimeSpan.FromMinutes(5);
    private readonly JsonSerializerOptions _canonicalJsonOptions;

    public RegisterCreationOrchestrator(
        ILogger<RegisterCreationOrchestrator> logger,
        RegisterManager registerManager,
        TransactionManager transactionManager,
        IWalletServiceClient walletClient,
        IHashProvider hashProvider,
        ICryptoModule cryptoModule,
        IValidatorServiceClient validatorClient,
        ISystemWalletSigningService signingService,
        IPendingRegistrationStore pendingStore,
        IPeerServiceClient peerClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registerManager = registerManager ?? throw new ArgumentNullException(nameof(registerManager));
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
        _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        _peerClient = peerClient ?? throw new ArgumentNullException(nameof(peerClient));

        // Configure JSON serialization for canonical form
        // UnsafeRelaxedJsonEscaping ensures characters like '+' in DateTimeOffset and base64
        // are NOT escaped to \u002B — critical for deterministic hash computation.
        _canonicalJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <summary>
    /// Initiates register creation (Phase 1): generates unsigned control record
    /// </summary>
    public async Task<InitiateRegisterCreationResponse> InitiateAsync(
        InitiateRegisterCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Owners == null || request.Owners.Count == 0)
        {
            throw new ArgumentException("At least one owner is required to create a register.");
        }

        _logger.LogInformation(
            "Initiating register creation for name '{Name}' in tenant '{TenantId}' with {OwnerCount} owner(s)",
            request.Name,
            request.TenantId,
            request.Owners.Count);

        // Use pre-determined register ID if provided, otherwise generate a new one
        var registerId = !string.IsNullOrWhiteSpace(request.RegisterId)
            ? request.RegisterId
            : Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.Add(_pendingExpirationTime);
        var nonce = GenerateNonce();

        // Create attestations to sign for each owner
        var attestationsToSign = new List<AttestationToSign>();

        _logger.LogInformation(
            "Processing owners for register {RegisterId}: {OwnerCount} owner(s) provided",
            registerId,
            request.Owners?.Count ?? 0);

        // Attestation hashes to store for verification during finalization
        var attestationHashes = new Dictionary<string, byte[]>();

        // Generate attestation data for each owner
        foreach (var owner in request.Owners ?? new List<OwnerInfo>())
        {
            var attestationData = new AttestationSigningData
            {
                Role = RegisterRole.Owner,
                Subject = $"did:sorcha:w:{owner.WalletId}",
                RegisterId = registerId,
                RegisterName = request.Name,
                GrantedAt = createdAt
            };

            // Serialize to canonical JSON and compute SHA-256 hash
            var canonicalJson = JsonSerializer.Serialize(attestationData, _canonicalJsonOptions);
            var hashBytes = _hashProvider.ComputeHash(
                Encoding.UTF8.GetBytes(canonicalJson),
                Sorcha.Cryptography.Enums.HashType.SHA256);
            var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

            // Store hash for verification during finalization
            var hashKey = $"{attestationData.Role}:{attestationData.Subject}";
            attestationHashes[hashKey] = hashBytes;

            _logger.LogDebug(
                "Created attestation for owner {UserId}: key={Key}, hash={Hash}",
                owner.UserId, hashKey, hashHex);

            attestationsToSign.Add(new AttestationToSign
            {
                UserId = owner.UserId,
                WalletId = owner.WalletId,
                Role = RegisterRole.Owner,
                AttestationData = attestationData,
                DataToSign = hashHex  // Hex-encoded SHA-256 hash
            });
        }

        // Generate attestation data for additional administrators
        if (request.AdditionalAdmins != null)
        {
            foreach (var admin in request.AdditionalAdmins)
            {
                var attestationData = new AttestationSigningData
                {
                    Role = admin.Role,
                    Subject = $"did:sorcha:w:{admin.WalletId}",
                    RegisterId = registerId,
                    RegisterName = request.Name,
                    GrantedAt = createdAt
                };

                // Serialize to canonical JSON and compute SHA-256 hash
                var canonicalJson = JsonSerializer.Serialize(attestationData, _canonicalJsonOptions);
                var hashBytes = _hashProvider.ComputeHash(
                    Encoding.UTF8.GetBytes(canonicalJson),
                    Sorcha.Cryptography.Enums.HashType.SHA256);
                var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

                // Store hash for verification during finalization
                var hashKey = $"{attestationData.Role}:{attestationData.Subject}";
                attestationHashes[hashKey] = hashBytes;

                _logger.LogDebug(
                    "Created attestation for admin {UserId}: key={Key}, hash={Hash}",
                    admin.UserId, hashKey, hashHex);

                attestationsToSign.Add(new AttestationToSign
                {
                    UserId = admin.UserId,
                    WalletId = admin.WalletId,
                    Role = admin.Role,
                    AttestationData = attestationData,
                    DataToSign = hashHex  // Hex-encoded SHA-256 hash
                });
            }
        }

        // Store pending registration with register metadata and attestation hashes
        var pending = new PendingRegistration
        {
            RegisterId = registerId,
            ControlRecord = new RegisterControlRecord
            {
                RegisterId = registerId,
                Name = request.Name,
                Description = request.Description,
                TenantId = request.TenantId,
                CreatedAt = createdAt,
                Metadata = request.Metadata,
                RegisterPolicy = request.Policy ?? RegisterPolicy.CreateDefault(),
                Attestations = new List<RegisterAttestation>() // Will be filled during finalization
            },
            ControlRecordHash = string.Empty,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            Nonce = nonce,
            AttestationHashes = attestationHashes,
            Advertise = request.Advertise
        };

        _pendingStore.Add(registerId, pending);

        // Schedule cleanup of expired pending registrations
        _ = CleanupExpiredPendingRegistrationsAsync();

        _logger.LogInformation(
            "Register initiation created with ID {RegisterId}, {AttestationCount} attestation(s) to sign, expires at {ExpiresAt}",
            registerId,
            attestationsToSign.Count,
            expiresAt);

        return new InitiateRegisterCreationResponse
        {
            RegisterId = registerId,
            AttestationsToSign = attestationsToSign,
            ExpiresAt = expiresAt,
            Nonce = nonce
        };
    }

    /// <summary>
    /// Finalizes register creation (Phase 2): verifies signatures and creates register
    /// </summary>
    public async Task<FinalizeRegisterCreationResponse> FinalizeAsync(
        FinalizeRegisterCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Finalizing register creation for ID {RegisterId} with {AttestationCount} signed attestations",
            request.RegisterId,
            request.SignedAttestations.Count);

        // Retrieve and remove pending registration
        if (!_pendingStore.TryRemove(request.RegisterId, out var pending))
        {
            _logger.LogWarning("Pending registration not found for ID {RegisterId}", request.RegisterId);
            throw new InvalidOperationException($"Pending registration not found for register ID {request.RegisterId}");
        }

        // Verify nonce (replay protection)
        if (pending!.Nonce != request.Nonce)
        {
            _logger.LogWarning(
                "Invalid nonce for register {RegisterId}: expected {Expected}, got {Actual}",
                request.RegisterId,
                pending.Nonce,
                request.Nonce);
            throw new UnauthorizedAccessException("Invalid nonce - possible replay attack");
        }

        // Check expiration (already removed from store above)
        if (pending.IsExpired())
        {
            _logger.LogWarning("Pending registration expired for ID {RegisterId}", request.RegisterId);
            throw new InvalidOperationException($"Pending registration expired for register ID {request.RegisterId}");
        }

        // Verify all attestation signatures against stored hashes from initiation
        await VerifyAttestationsAsync(request.SignedAttestations, pending, cancellationToken);

        // Construct control record from verified attestations
        var controlRecord = new RegisterControlRecord
        {
            RegisterId = pending.ControlRecord.RegisterId,
            Name = pending.ControlRecord.Name,
            Description = pending.ControlRecord.Description,
            TenantId = pending.ControlRecord.TenantId,
            CreatedAt = pending.ControlRecord.CreatedAt,
            Metadata = pending.ControlRecord.Metadata,
            CryptoPolicy = CryptoPolicy.CreateDefault(),
            RegisterPolicy = pending.ControlRecord.RegisterPolicy,
            Attestations = request.SignedAttestations.Select(sa => new RegisterAttestation
            {
                Role = sa.AttestationData.Role,
                Subject = sa.AttestationData.Subject,
                PublicKey = sa.PublicKey,
                Signature = sa.Signature,
                Algorithm = sa.Algorithm,
                GrantedAt = sa.AttestationData.GrantedAt
            }).ToList()
        };

        // Validate constructed control record
        var validationErrors = ValidateControlRecord(controlRecord);
        if (validationErrors.Any())
        {
            _logger.LogWarning(
                "Control record validation failed for {RegisterId}: {Errors}",
                request.RegisterId,
                string.Join(", ", validationErrors));
            throw new ArgumentException($"Control record validation failed: {string.Join(", ", validationErrors)}");
        }

        _logger.LogInformation(
            "Control record constructed successfully with {AttestationCount} verified attestations",
            controlRecord.Attestations.Count);

        // Create genesis transaction with control record payload (includes real PayloadHash)
        var genesisTransaction = CreateGenesisTransaction(pending.RegisterId, controlRecord);

        _logger.LogInformation(
            "Created genesis transaction {TransactionId} for register {RegisterId}",
            genesisTransaction.TxId,
            pending.RegisterId);

        // Use the same canonical JSON that was hashed in CreateGenesisTransaction
        // to ensure deterministic hash verification at the Validator.
        // Decode from base64 (stored in Payloads[0].Data) to get the exact bytes that were hashed.
        var canonicalPayloadBytes = Base64Url.DecodeFromChars(genesisTransaction.Payloads[0].Data);
        var canonicalPayloadJson = Encoding.UTF8.GetString(canonicalPayloadBytes);
        var payloadHash = genesisTransaction.Payloads[0].Hash;

        // Sign with system wallet
        var signResult = await _signingService.SignAsync(
            registerId: pending.RegisterId,
            txId: genesisTransaction.TxId,
            payloadHash: payloadHash,
            derivationPath: "sorcha:register-control",
            transactionType: "Genesis",
            cancellationToken);

        // Only the system wallet signature goes at the transaction level.
        // Attestation signatures are embedded in the control record payload
        // and were already verified during FinalizeAsync. The Validator verifies
        // all transaction-level signatures against SHA256("{TxId}:{PayloadHash}"),
        // which attestation signatures were NOT signed against.
        var systemSignature = new SignatureInfo
        {
            PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
            SignatureValue = Base64Url.EncodeToString(signResult.Signature),
            Algorithm = signResult.Algorithm
        };

        // Submit through unified generic endpoint
        var submissionRequest = new TransactionSubmission
        {
            TransactionId = genesisTransaction.TxId,
            RegisterId = pending.RegisterId,
            BlueprintId = GenesisConstants.BlueprintId,
            ActionId = "register-creation",
            Payload = JsonDocument.Parse(canonicalPayloadJson).RootElement,
            PayloadHash = payloadHash,
            Signatures = new List<SignatureInfo> { systemSignature },
            CreatedAt = controlRecord.CreatedAt,
            Metadata = new Dictionary<string, string>
            {
                ["Type"] = "Genesis",
                ["RegisterName"] = controlRecord.Name,
                ["TenantId"] = controlRecord.TenantId,
                ["SystemWalletAddress"] = signResult.WalletAddress
            }
        };

        var submissionResult = await _validatorClient.SubmitTransactionAsync(submissionRequest, cancellationToken);

        if (!submissionResult.Success)
        {
            _logger.LogError(
                "Failed to submit genesis transaction {TransactionId} to Validator Service for register {RegisterId}: {Error}",
                genesisTransaction.TxId, pending.RegisterId, submissionResult.ErrorMessage);
            throw new InvalidOperationException(
                $"Genesis transaction submission failed for register {pending.RegisterId}: {submissionResult.ErrorMessage}. " +
                "The register was NOT created. Retry the full initiate/finalize flow.");
        }

        _logger.LogInformation(
            "Genesis transaction {TransactionId} submitted to Validator Service successfully via generic endpoint",
            genesisTransaction.TxId);

        // Only persist register AFTER genesis succeeds (atomic guarantee)
        // Use the register ID from the pending registration (established during initiation)
        var register = await _registerManager.CreateRegisterAsync(
            controlRecord.Name,
            controlRecord.TenantId,
            advertise: pending.Advertise,
            isFullReplica: true,
            registerId: pending.RegisterId,
            description: controlRecord.Description,
            cancellationToken);

        _logger.LogInformation("Created register {RegisterId} in database after genesis success", register.Id);

        // Set register Online after successful creation
        // SignalR notifications (RegisterStatusChanged, RegisterCreated) handled by RegisterEventBridgeService
        // via events published by RegisterManager.CreateRegisterAsync and UpdateRegisterStatusAsync
        register = await _registerManager.UpdateRegisterStatusAsync(register.Id, RegisterStatus.Online, cancellationToken);

        _logger.LogInformation("Register {RegisterId} set to Online", register.Id);

        // Notify Peer Service to advertise register if requested (fire-and-forget)
        if (pending.Advertise)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _peerClient.AdvertiseRegisterAsync(register.Id, isPublic: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to notify Peer Service about register {RegisterId} advertisement. Register was created successfully.",
                        register.Id);
                }
            });
        }

        // NOTE: Genesis transaction remains in Validator memory pool
        // It will be written to Register Service database after docket creation
        // Validator Service handles the write after successful docket build

        return new FinalizeRegisterCreationResponse
        {
            RegisterId = register.Id,
            Status = "created",
            GenesisTransactionId = genesisTransaction.TxId,
            GenesisDocketId = "0",
            CreatedAt = register.CreatedAt
        };
    }

    /// <summary>
    /// Validates control record structure
    /// </summary>
    private List<string> ValidateControlRecord(RegisterControlRecord controlRecord)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(controlRecord.RegisterId))
            errors.Add("RegisterId is required");

        if (string.IsNullOrWhiteSpace(controlRecord.Name))
            errors.Add("Name is required");

        if (controlRecord.Name.Length > 38)
            errors.Add("Name must be 38 characters or less");

        if (controlRecord.Description?.Length > 500)
            errors.Add("Description must be 500 characters or less");

        if (!controlRecord.Attestations.Any())
            errors.Add("At least one attestation is required");

        if (!controlRecord.HasOwnerAttestation())
            errors.Add("At least one Owner attestation is required");

        if (controlRecord.Attestations.Count > 10)
            errors.Add("Maximum 10 attestations allowed");

        // Validate each attestation
        foreach (var attestation in controlRecord.Attestations)
        {
            if (string.IsNullOrWhiteSpace(attestation.Subject))
                errors.Add($"Attestation subject is required");

            if (string.IsNullOrWhiteSpace(attestation.PublicKey))
                errors.Add($"Attestation public key is required for {attestation.Subject}");

            if (string.IsNullOrWhiteSpace(attestation.Signature))
                errors.Add($"Attestation signature is required for {attestation.Subject}");
        }

        return errors;
    }

    /// <summary>
    /// Verifies all attestation signatures against stored hashes from initiation.
    /// Uses stored hash bytes instead of re-serializing attestation data,
    /// eliminating JSON canonicalization fragility.
    /// </summary>
    private async Task VerifyAttestationsAsync(
        List<SignedAttestation> signedAttestations,
        PendingRegistration pending,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Verifying {Count} signed attestations for register {RegisterId} using stored hashes",
            signedAttestations.Count,
            pending.RegisterId);

        foreach (var signedAttestation in signedAttestations)
        {
            try
            {
                // Look up stored hash by role:subject key
                var hashKey = $"{signedAttestation.AttestationData.Role}:{signedAttestation.AttestationData.Subject}";

                if (!pending.AttestationHashes.TryGetValue(hashKey, out var storedHashBytes))
                {
                    _logger.LogWarning(
                        "No stored hash found for attestation key {HashKey} in register {RegisterId}",
                        hashKey, pending.RegisterId);
                    throw new ArgumentException(
                        $"Unknown attestation: {signedAttestation.AttestationData.Subject} ({signedAttestation.AttestationData.Role})");
                }

                _logger.LogDebug(
                    "Verifying attestation: key={Key}, storedHashLen={HashLen}",
                    hashKey, storedHashBytes.Length);

                // Convert base64/base64url public key and signature to bytes
                var publicKeyBytes = Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(signedAttestation.PublicKey);
                var signatureBytes = Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(signedAttestation.Signature);

                // Verify signature against stored hash using Sorcha.Cryptography
                var verifyResult = await _cryptoModule.VerifyAsync(
                    signatureBytes,
                    storedHashBytes,
                    MapAlgorithm(signedAttestation.Algorithm),
                    publicKeyBytes,
                    cancellationToken);

                if (verifyResult != Sorcha.Cryptography.Enums.CryptoStatus.Success)
                {
                    _logger.LogWarning(
                        "Signature verification failed for attestation: key={Key}, result={Result}",
                        hashKey, verifyResult);
                    throw new UnauthorizedAccessException(
                        $"Invalid signature for attestation: {signedAttestation.AttestationData.Subject} ({signedAttestation.AttestationData.Role})");
                }

                _logger.LogDebug(
                    "Verified signature for {Subject} ({Role})",
                    signedAttestation.AttestationData.Subject,
                    signedAttestation.AttestationData.Role);
            }
            catch (FormatException ex)
            {
                _logger.LogError(
                    ex,
                    "Invalid base64 encoding in attestation for {Subject}",
                    signedAttestation.AttestationData.Subject);
                throw new ArgumentException(
                    $"Invalid base64 encoding in attestation for {signedAttestation.AttestationData.Subject}",
                    ex);
            }
        }

        _logger.LogInformation(
            "All {Count} attestations verified successfully for register {RegisterId}",
            signedAttestations.Count,
            pending.RegisterId);
    }

    /// <summary>
    /// Creates a genesis transaction with control record payload and computed PayloadHash
    /// </summary>
    private TransactionModel CreateGenesisTransaction(string registerId, RegisterControlRecord controlRecord)
    {
        // Serialize to canonical form, then re-canonicalize through JsonElement round-trip.
        // This ensures the hash matches what the Validator computes when it receives the payload
        // and re-canonicalizes with the same options (compact, UnsafeRelaxedJsonEscaping).
        var controlRecordJson = JsonSerializer.Serialize(controlRecord, _canonicalJsonOptions);
        using var doc = JsonDocument.Parse(controlRecordJson);
        var canonicalJson = JsonSerializer.Serialize(doc.RootElement, _canonicalJsonOptions);
        var controlRecordBytes = Encoding.UTF8.GetBytes(canonicalJson);

        // Compute actual SHA-256 hash of the serialized control record payload
        var payloadHash = _hashProvider.ComputeHash(controlRecordBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
        var payloadHashHex = Convert.ToHexString(payloadHash).ToLowerInvariant();

        // Generate a proper 64-character transaction ID by hashing "genesis-{registerId}"
        var genesisIdBytes = Encoding.UTF8.GetBytes($"genesis-{registerId}");
        var genesisIdHash = _hashProvider.ComputeHash(genesisIdBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
        var genesisTxId = Convert.ToHexString(genesisIdHash).ToLowerInvariant();

        return new TransactionModel
        {
            TxId = genesisTxId,
            RegisterId = registerId,
            SenderWallet = "system", // System transaction
            TimeStamp = controlRecord.CreatedAt.UtcDateTime,
            PrevTxId = string.Empty, // Genesis has no previous transaction
            PayloadCount = 1, // One payload containing the control record
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = Base64Url.EncodeToString(controlRecordBytes),
                    WalletAccess = controlRecord.Attestations.Select(a => a.Subject).ToArray(),
                    Hash = payloadHashHex,
                    ContentType = "application/json",
                    ContentEncoding = "base64url"
                }
            },
            MetaData = new TransactionMetaData
            {
                RegisterId = registerId,
                TransactionType = TransactionType.Control
            },
            Version = 1,
            Signature = string.Empty // Signed by Validator Service system wallet
        };
    }

    /// <summary>
    /// Maps SignatureAlgorithm to WalletNetworks (byte) for Sorcha.Cryptography
    /// </summary>
    private byte MapAlgorithm(SignatureAlgorithm algorithm)
    {
        return algorithm switch
        {
            SignatureAlgorithm.ED25519 => (byte)Sorcha.Cryptography.Enums.WalletNetworks.ED25519,
            SignatureAlgorithm.NISTP256 => (byte)Sorcha.Cryptography.Enums.WalletNetworks.NISTP256,
            SignatureAlgorithm.RSA4096 => (byte)Sorcha.Cryptography.Enums.WalletNetworks.RSA4096,
            SignatureAlgorithm.ML_DSA_65 => (byte)Sorcha.Cryptography.Enums.WalletNetworks.ML_DSA_65,
            SignatureAlgorithm.SLH_DSA_128s => (byte)Sorcha.Cryptography.Enums.WalletNetworks.SLH_DSA_128s,
            _ => throw new ArgumentException($"Unsupported signature algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// Generates a cryptographic nonce for replay protection
    /// </summary>
    private string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// Cleans up expired pending registrations
    /// </summary>
    private async Task CleanupExpiredPendingRegistrationsAsync()
    {
        await Task.Delay(TimeSpan.FromMinutes(1)); // Run every minute
        _pendingStore.CleanupExpired();
    }
}

/// <summary>
/// Interface for register creation orchestration
/// </summary>
public interface IRegisterCreationOrchestrator
{
    /// <summary>
    /// Initiates register creation (Phase 1): generates unsigned control record
    /// </summary>
    Task<InitiateRegisterCreationResponse> InitiateAsync(
        InitiateRegisterCreationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes register creation (Phase 2): verifies signatures and creates register
    /// </summary>
    Task<FinalizeRegisterCreationResponse> FinalizeAsync(
        FinalizeRegisterCreationRequest request,
        CancellationToken cancellationToken = default);
}
