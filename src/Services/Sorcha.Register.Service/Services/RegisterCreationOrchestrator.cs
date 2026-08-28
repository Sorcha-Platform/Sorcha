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
using Sorcha.Register.Service.Services.Interfaces;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.Wallet.Contracts.Constants;

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
    private readonly ITenantSubscriptionClient _tenantSubscriptionClient;
    private readonly IBloomFilterRebuilder _bloomFilterRebuilder;
    private readonly RelationshipChangeNotifier _relationshipNotifier;

    private readonly TimeSpan _pendingExpirationTime = TimeSpan.FromMinutes(5);

    // Shared canonical JSON options live in Sorcha.Register.Models so every service
    // that serialises register-scoped payloads agrees on the byte shape.
    private static readonly JsonSerializerOptions _canonicalJsonOptions =
        RegisterSerializationOptions.Canonical;

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
        IPeerServiceClient peerClient,
        ITenantSubscriptionClient tenantSubscriptionClient,
        IBloomFilterRebuilder bloomFilterRebuilder,
        RelationshipChangeNotifier relationshipNotifier)
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
        _tenantSubscriptionClient = tenantSubscriptionClient ?? throw new ArgumentNullException(nameof(tenantSubscriptionClient));
        _bloomFilterRebuilder = bloomFilterRebuilder ?? throw new ArgumentNullException(nameof(bloomFilterRebuilder));
        _relationshipNotifier = relationshipNotifier ?? throw new ArgumentNullException(nameof(relationshipNotifier));
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

        // Validate the register name at the INPUT BOUNDARY (fail-fast), not deep inside FinalizeAsync
        // after attestations have already been signed. The same 1..38 rule is enforced again by
        // ValidateControlRecord (finalize) and RegisterManager.CreateRegisterAsync (persist); checking
        // it here turns a silent late failure into a clear 400 on the very first call. (F174/F175 —
        // a 39-char name previously threw only at finalize and was masked as a partial success.)
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Register name is required.");
        }
        if (request.Name.Length > 38)
        {
            throw new ArgumentException(
                $"Register name must be 38 characters or less (was {request.Name.Length}: '{request.Name}').");
        }

        _logger.LogInformation(
            "Initiating register creation for name '{Name}' with {OwnerCount} owner(s)",
            request.Name,
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
            Advertise = request.Advertise,
            DevMode = request.DevMode,
            Purpose = request.Purpose
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
    /// Finalizes register creation (Phase 2): verifies signatures and creates register.
    /// When <paramref name="callerOrganizationId"/> is non-empty, the owning
    /// organisation is immediately subscribed via a service-to-service call to
    /// the Tenant Service's internal endpoint. Failures there are logged but do
    /// NOT fail finalisation — the register has already been sealed and can be
    /// subscribed manually later.
    /// </summary>
    public async Task<FinalizeRegisterCreationResponse> FinalizeAsync(
        FinalizeRegisterCreationRequest request,
        Guid callerOrganizationId = default,
        Guid callerUserId = default,
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

        // Bake the register's DevMode posture into the genesis crypto policy so it is part of the
        // immutable, signed, replicated genesis (a SyncOnly replica reads it from the synced
        // control record). One-way: promotable to Normal via a crypto-policy update, never back —
        // validator-enforced.
        var genesisCryptoPolicy = CryptoPolicy.CreateDefault();
        genesisCryptoPolicy.DevMode = pending.DevMode;

        // Construct control record from verified attestations
        var controlRecord = new RegisterControlRecord
        {
            RegisterId = pending.ControlRecord.RegisterId,
            Name = pending.ControlRecord.Name,
            Description = pending.ControlRecord.Description,
            CreatedAt = pending.ControlRecord.CreatedAt,
            Metadata = pending.ControlRecord.Metadata,
            CryptoPolicy = genesisCryptoPolicy,
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

        // Populate validator roster (FR-001, FR-014)
        // If an external roster is provided (future System Register, FR-014), use it.
        // Otherwise, derive the local validator's docket-signing key from the system wallet.
        //
        // Contract (Feature 108): the wallet we sign-under here is the one identified by
        // SystemWalletSigning:ValidatorId, which MUST match Validator:ValidatorId on the
        // validator service of this node. Both services then derive the same
        // sorcha:docket-signing key from the same underlying wallet, and the validator
        // recognises the roster entry as its own. See SystemWalletSigningOptions.ValidatorId
        // and ValidatorConfiguration.ValidatorId for the contract.
        if (controlRecord.Validators == null)
        {
            // TODO: Replace with IWalletServiceClient.GetDerivedPublicKeyAsync() when available.
            // Currently we sign a zeroed hash to obtain the derived public key as a side-effect.
            // The signature itself is discarded — only the PublicKey from the result is used.
            var docketSignResult = await _signingService.SignAsync(
                registerId: pending.RegisterId,
                txId: "validator-roster-key-derivation",
                payloadHash: "0000000000000000000000000000000000000000000000000000000000000000",
                derivationPath: SorchaDerivationPaths.DocketSigning,
                transactionType: "ValidatorKeyDerivation",
                cancellationToken);

            // Feature 196 (#1591): the roster must also record who may PUBLISH definitions to this
            // register. The Validator grants the blueprint-publication exemption — which waives six
            // rules including VAL_BP_002 sender authorisation — only to a signer holding an active
            // entry under sorcha:blueprint-publish. Without this entry the register accepts no
            // blueprint publications at all, which is the correct fail-closed direction but makes
            // the register useless.
            //
            // Provisioned HERE, in the roster snapshot that already exists, rather than as a
            // follow-up control transaction. GovernanceRosterService reconstructs the roster as
            // latest-control-tx-WITH-A-ROSTER wins — it does not merge — so a later transaction
            // carrying only validators would replace the snapshot and drop every governance
            // attestation with it, refusing all governance platform-wide. That is #1515's shape.
            var publishSignResult = await _signingService.SignAsync(
                registerId: pending.RegisterId,
                txId: "publisher-roster-key-derivation",
                payloadHash: "0000000000000000000000000000000000000000000000000000000000000000",
                derivationPath: SorchaDerivationPaths.BlueprintPublish,
                transactionType: "ValidatorKeyDerivation",
                cancellationToken);

            controlRecord.Validators = new ValidatorRoster
            {
                Validators =
                [
                    new ValidatorRosterEntry
                    {
                        ValidatorId = docketSignResult.WalletAddress,
                        PublicKey = Convert.ToBase64String(docketSignResult.PublicKey),
                        Algorithm = Enum.TryParse<SignatureAlgorithm>(docketSignResult.Algorithm, true, out var alg)
                            ? alg : SignatureAlgorithm.ED25519,
                        DerivationContext = SorchaDerivationPaths.DocketSigning,
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = controlRecord.CreatedAt
                    },
                    new ValidatorRosterEntry
                    {
                        // Same node id as the docket-signing entry: one node, two purpose keys.
                        ValidatorId = docketSignResult.WalletAddress,
                        PublicKey = Convert.ToBase64String(publishSignResult.PublicKey),
                        Algorithm = Enum.TryParse<SignatureAlgorithm>(publishSignResult.Algorithm, true, out var pubAlg)
                            ? pubAlg : SignatureAlgorithm.ED25519,
                        DerivationContext = SorchaDerivationPaths.BlueprintPublish,
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = controlRecord.CreatedAt
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            };

            _logger.LogInformation(
                "Populated validator roster for register {RegisterId} with local validator {ValidatorId} "
                + "(docket signing) and publisher {PublisherId} (blueprint publication)",
                pending.RegisterId, docketSignResult.WalletAddress, publishSignResult.WalletAddress);
        }

        // Validate validator roster (FR-010)
        var rosterErrors = controlRecord.Validators.Validate();
        if (rosterErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Validator roster validation failed: {string.Join(", ", rosterErrors)}");
        }

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

        // Extract the owner's wallet address from the first owner attestation subject.
        // Subject format: "did:sorcha:w:{walletAddress}"
        const string didPrefix = "did:sorcha:w:";
        var ownerAttestation = controlRecord.Attestations
            .FirstOrDefault(a => a.Role == RegisterRole.Owner)
            ?? throw new InvalidOperationException("Control record has no Owner attestation");
        var ownerWalletAddress = ownerAttestation.Subject.StartsWith(didPrefix, StringComparison.Ordinal)
            ? ownerAttestation.Subject[didPrefix.Length..]
            : throw new InvalidOperationException(
                $"Owner attestation subject '{ownerAttestation.Subject}' does not match expected DID format '{didPrefix}...'");

        // Create genesis transaction with control record payload (includes real PayloadHash)
        var genesisTransaction = CreateGenesisTransaction(pending.RegisterId, controlRecord, ownerWalletAddress);

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
            derivationPath: SorchaDerivationPaths.RegisterControl,
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
        // Use the register ID from the pending registration (established during initiation).
        // Stash the control record so local-relationship derivation can resolve the roster
        // before the genesis docket is sealed — this is what lets the validator enrol for
        // monitoring and then seal the genesis tx that's waiting in its pool.
        var register = await _registerManager.CreateRegisterAsync(
            controlRecord.Name,
            advertise: pending.Advertise,
            isFullReplica: true,
            registerId: pending.RegisterId,
            description: controlRecord.Description,
            devMode: pending.DevMode,
            purpose: pending.Purpose,
            initialControlRecord: controlRecord,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Created register {RegisterId} in database after genesis success", register.Id);

        // Fire relationship-changed event so the local validator enrols for monitoring
        // without waiting for the next 30-second safety poll. The validator's
        // RegisterMonitoringBootstrap will re-reconcile, find this register via the
        // stashed control record, and start sealing the genesis tx waiting in its pool.
        // Fire-and-forget — Redis publish failure is non-fatal, the safety poll covers it.
        // ContinueWith logs any task-level escape so the unobserved-task pipeline doesn't
        // swallow shutdown / scheduler faults.
        _ = Task.Run(() => _relationshipNotifier.PublishIfChangedAsync(register.Id))
            .ContinueWith(
                t => _logger.LogWarning(t.Exception,
                    "RelationshipChangeNotifier.PublishIfChangedAsync escaped for register {RegisterId}",
                    register.Id),
                TaskContinuationOptions.OnlyOnFaulted);

        // Set register Online after successful creation
        // SignalR notifications (RegisterStatusChanged, RegisterCreated) handled by RegisterEventBridgeService
        // via events published by RegisterManager.CreateRegisterAsync and UpdateRegisterStatusAsync
        register = await _registerManager.UpdateRegisterStatusAsync(register.Id, RegisterStatus.Online, cancellationToken);

        _logger.LogInformation("Register {RegisterId} set to Online", register.Id);

        // Best-effort fan-in; 10s timeout caps wallet-svc latency.
        using var bloomCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bloomCts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var bloomStats = await _bloomFilterRebuilder.RebuildAsync(register.Id, bloomCts.Token);
            _logger.LogInformation(
                "Initialised bloom filter for new register {RegisterId} with {AddressCount} addresses.",
                register.Id, bloomStats.AddressCount);
        }
        catch (Exception ex) when (ex is OperationCanceledException
                                      or global::Grpc.Core.RpcException
                                      or IOException
                                      or InvalidOperationException
                                      or TimeoutException)
        {
            _logger.LogWarning(ex,
                "Failed to initialise bloom filter for new register {RegisterId}; reconciliation deferred to next startup-rebuild or admin /rebuild-index.",
                register.Id);
        }

        // Notify Peer Service to advertise register if requested (fire-and-forget)
        if (pending.Advertise)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _peerClient.AdvertiseRegisterAsync(
                        register.Id,
                        isPublic: true,
                        name: controlRecord.Name,
                        description: controlRecord.Description);
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

        // Create the owner subscription via a service-to-service call to the
        // Tenant Service. Attestation signatures have already been verified above,
        // so the Register Service is in a position to vouch for ownership without
        // the admin role check that gates the public subscribe endpoint. Failure
        // here is logged but non-fatal — the register itself is already sealed
        // and a manual subscribe will reconcile it.
        if (callerOrganizationId != Guid.Empty)
        {
            try
            {
                await _tenantSubscriptionClient.CreateOwnerSubscriptionAsync(
                    callerOrganizationId,
                    register.Id,
                    controlRecord.Name,
                    callerUserId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Owner subscription call failed for register {RegisterId} (org {OrgId}). Register was created successfully; subscribe manually to reconcile.",
                    register.Id, callerOrganizationId);
            }
        }
        else
        {
            _logger.LogDebug(
                "Register {RegisterId} finalised without a caller org_id — owner subscription skipped (e.g. system bootstrap path).",
                register.Id);
        }

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
    private TransactionModel CreateGenesisTransaction(string registerId, RegisterControlRecord controlRecord, string ownerWalletAddress)
    {
        // Wrap in ControlTransactionPayload so GovernanceRosterService can read the genesis
        // payload via the same path as governance-proposal control transactions. The non-genesis
        // commit at Program.cs:2204 already wraps in this shape; the genesis was the lone outlier
        // — bare RegisterControlRecord deserialises silently into a default-empty
        // ControlTransactionPayload, which surfaced as `members: []` on /governance/roster and
        // led to a confusing 403 from the F142 PublishGate.
        var payload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = controlRecord,
            Operation = null   // genesis has no producing operation
        };

        // Serialize to canonical form, then re-canonicalize through JsonElement round-trip.
        // This ensures the hash matches what the Validator computes when it receives the payload
        // and re-canonicalizes with the same options (compact, UnsafeRelaxedJsonEscaping).
        var controlRecordJson = JsonSerializer.Serialize(payload, _canonicalJsonOptions);
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
            SenderWallet = ownerWalletAddress, // Owner who initiated register creation
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
    /// Finalizes register creation (Phase 2): verifies signatures and creates register.
    /// After successful persistence, the orchestrator calls the Tenant Service's
    /// internal owner-subscription endpoint using <paramref name="callerOrganizationId"/>
    /// and <paramref name="callerUserId"/> (both resolved from the authenticated
    /// caller's JWT claims). Pass <see cref="Guid.Empty"/> for either value to skip
    /// the subscription step (e.g. bootstrapper contexts with no user identity).
    /// </summary>
    Task<FinalizeRegisterCreationResponse> FinalizeAsync(
        FinalizeRegisterCreationRequest request,
        Guid callerOrganizationId = default,
        Guid callerUserId = default,
        CancellationToken cancellationToken = default);
}
