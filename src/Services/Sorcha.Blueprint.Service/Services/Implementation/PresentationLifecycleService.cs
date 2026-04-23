// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.ServiceClients.Haip;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using CredentialRequirementModel = Sorcha.Blueprint.Models.Credentials.CredentialRequirement;
using BlueprintPresentationConfig = Sorcha.Blueprint.Models.BlueprintPresentationConfig;
using OutcomeDetailLevel = Sorcha.Blueprint.Models.OutcomeDetailLevel;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 111 — orchestrates the three-event Timebound Presentation Lifecycle.
/// US1 scope: InitiateAsync writes a PresentationInitiated transaction on every
/// submission and stores pending state; HandleOutcomeAsync and HandleAbandonmentAsync
/// land in later user stories.
/// </summary>
public sealed class PresentationLifecycleService : IPresentationLifecycleService
{
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.PresentationLifecycle");

    private readonly ITransactionBuilderService _transactionBuilder;
    private readonly IWalletServiceClient _walletClient;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly IHaipServiceClient? _haipClient;
    private readonly IPendingPresentationStore _pendingStore;
    private readonly IOptions<PresentationLifecycleOptions> _options;
    private readonly ILogger<PresentationLifecycleService> _logger;

    public PresentationLifecycleService(
        ITransactionBuilderService transactionBuilder,
        IWalletServiceClient walletClient,
        IValidatorServiceClient validatorClient,
        IPendingPresentationStore pendingStore,
        IOptions<PresentationLifecycleOptions> options,
        ILogger<PresentationLifecycleService> logger,
        IHaipServiceClient? haipClient = null)
    {
        _transactionBuilder = transactionBuilder ?? throw new ArgumentNullException(nameof(transactionBuilder));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _haipClient = haipClient;
    }

    public async Task<PresentationInitiationResult> InitiateAsync(
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        CredentialRequirementModel haipRequirement,
        string submitterWallet,
        string? delegationToken,
        IReadOnlyDictionary<string, object> draftPayload,
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(haipRequirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(submitterWallet);
        ArgumentNullException.ThrowIfNull(draftPayload);

        if (_haipClient is null)
        {
            throw new InvalidOperationException(
                "HAIP consumer client is not registered; cannot initiate HAIP presentation.");
        }

        using var activity = ActivitySource.StartActivity("presentation.initiated");
        activity?.SetTag("instance.id", instance.Id);
        activity?.SetTag("action.id", action.Id);
        activity?.SetTag("register.id", instance.RegisterId);
        activity?.SetTag("consumer", "haip");

        var config = ResolveConfig(blueprint);
        var validityWindow = config.PresentationValidityWindowSeconds
            ?? _options.Value.DefaultValidityWindowSeconds;

        // 1. Create the HAIP presentation request (QR URI + requestId).
        var requiredClaimNames = haipRequirement.RequiredClaims?
            .Select(c => c.ClaimName)
            .ToList();
        var haipResult = await _haipClient.CreatePresentationRequestAsync(
            haipRequirement.Type,
            requiredClaimNames,
            haipRequirement.AcceptedIssuers?.ToList(),
            cancellationToken);

        // 2. Compute requirements digest (SHA-256 of canonical requirements JSON).
        var digest = ComputeRequirementsDigest(haipRequirement);

        // 3. Store pending state in Redis so the callback and abandonment paths can
        //    reconstitute the draft action payload and blueprint config.
        var pending = new PendingPresentation
        {
            PresentationRequestId = haipResult.RequestId,
            InstanceId = Guid.Parse(instance.Id),
            ActionId = action.Id,
            RegisterId = instance.RegisterId,
            BlueprintId = blueprint.Id,
            SubmitterWallet = submitterWallet,
            ConsumerName = "haip",
            DraftPayloadJson = JsonSerializer.Serialize(draftPayload),
            CredentialRequirementDigestHex = Convert.ToHexString(digest).ToLowerInvariant(),
            DelegationToken = delegationToken,
            RecordAbandonment = config.RecordAbandonment,
            OutcomeDetailLevel = (config.OutcomeDetailLevel ?? OutcomeDetailLevel.Minimal)
                .ToString().ToLowerInvariant(),
            ValidityWindowSeconds = validityWindow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _pendingStore.StoreAsync(pending, cancellationToken);

        // 4. Build the presentation-initiated transaction (no credential data).
        var built = await _transactionBuilder.BuildPresentationInitiatedAsync(
            blueprint, instance, action,
            presentationRequestId: haipResult.RequestId,
            consumerName: "haip",
            requirementsDigest: digest,
            validityWindowSeconds: validityWindow,
            submitterWallet: submitterWallet,
            previousTransactionId: previousTransactionId,
            cancellationToken);

        // 5. Sign the transaction using the submitter wallet's default signing key.
        var signResult = await _walletClient.SignTransactionAsync(
            submitterWallet,
            built.SigningData,
            derivationPath: null,
            isPreHashed: false,
            cancellationToken);
        built.SenderWallet = submitterWallet;
        built.Signature = signResult.Signature;

        // 6. Fetch the next sequence number and submit to the validator.
        var nextSeqNum = await _validatorClient.GetNextSequenceNumberAsync(
            instance.RegisterId, submitterWallet, cancellationToken);
        var submission = built.ToTransactionSubmission(signResult, nextSeqNum);
        var validatorResult = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

        if (!validatorResult.Success)
        {
            _logger.LogWarning(
                "Validator rejected presentation-initiated transaction {TxId}: [{ErrorCode}] {ErrorMessage}",
                built.TxId, validatorResult.ErrorCode, validatorResult.ErrorMessage);
            // Attempt transient cleanup on failure.
            await _pendingStore.DeleteAsync(haipResult.RequestId, cancellationToken);
            throw new InvalidOperationException(
                $"Validator rejected presentation-initiated transaction {built.TxId}: " +
                $"[{validatorResult.ErrorCode}] {validatorResult.ErrorMessage}");
        }

        _logger.LogInformation(
            "PresentationInitiated tx {TxId} submitted for instance {InstanceId} action {ActionId} requestId {RequestId}",
            built.TxId, instance.Id, action.Id, haipResult.RequestId);
        activity?.SetTag("presentation.request_id", haipResult.RequestId.ToString());
        activity?.SetTag("tx.id", built.TxId);

        return new PresentationInitiationResult(
            PresentationRequestId: haipResult.RequestId,
            AuthorizationRequestUri: haipResult.AuthorizationRequestUri,
            RequestUri: haipResult.RequestUri,
            Nonce: haipResult.Nonce,
            ExpiresAt: haipResult.ExpiresAt,
            InitiatedTransactionId: built.TxId);
    }

    public Task<PresentationOutcomeResult> HandleOutcomeAsync(
        string consumerName,
        Guid presentationRequestId,
        object verifierPayload,
        CancellationToken cancellationToken = default)
    {
        // US2 scope — implementation lands with phase 4.
        throw new NotImplementedException(
            "PresentationLifecycleService.HandleOutcomeAsync arrives with User Story 2 (Feature 111 Phase 4).");
    }

    public Task HandleAbandonmentAsync(
        Guid presentationRequestId,
        CancellationToken cancellationToken = default)
    {
        // US4 scope — implementation lands with phase 6.
        throw new NotImplementedException(
            "PresentationLifecycleService.HandleAbandonmentAsync arrives with User Story 4 (Feature 111 Phase 6).");
    }

    /// <summary>
    /// Resolve the effective <see cref="BlueprintPresentationConfig"/> for a blueprint,
    /// falling back to a default record when the blueprint has no explicit config.
    /// </summary>
    private static BlueprintPresentationConfig ResolveConfig(BlueprintModel blueprint) =>
        blueprint.PresentationConfig ?? new BlueprintPresentationConfig();

    /// <summary>
    /// SHA-256 of the canonical JSON form of the action's credential requirements.
    /// Carried verbatim in the presentation-initiated transaction so any auditor can
    /// verify what the citizen was asked to present at the moment they attempted.
    /// </summary>
    private static byte[] ComputeRequirementsDigest(CredentialRequirementModel requirement)
    {
        var canonical = new
        {
            type = requirement.Type,
            acceptedIssuers = requirement.AcceptedIssuers?.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            requiredClaims = requirement.RequiredClaims?
                .OrderBy(c => c.ClaimName, StringComparer.Ordinal)
                .Select(c => c.ClaimName)
                .ToArray()
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return SHA256.HashData(json);
    }
}
