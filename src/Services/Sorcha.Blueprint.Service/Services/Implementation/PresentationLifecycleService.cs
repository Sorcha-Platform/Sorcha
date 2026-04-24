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
/// </summary>
/// <remarks>
/// Consumer-agnostic scope: <see cref="HandleOutcomeAsync"/> and
/// <see cref="HandleAbandonmentAsync"/> dispatch through the registered
/// <see cref="IPresentationConsumer"/> collection and carry no consumer-specific
/// code. <see cref="InitiateAsync"/> currently only supports HAIP — it calls
/// <see cref="IHaipServiceClient.CreatePresentationRequestAsync"/> inline and
/// hardcodes <c>consumerName = "haip"</c>. When non-HAIP consumers land, the
/// initiation path will extend <see cref="IPresentationConsumer"/> with a
/// contract like <c>CreateRequestAsync</c> and this class will delegate to the
/// matching consumer by name, mirroring the outcome path.
/// </remarks>
public sealed class PresentationLifecycleService : IPresentationLifecycleService
{
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.PresentationLifecycle");

    private readonly ITransactionBuilderService _transactionBuilder;
    private readonly IWalletServiceClient _walletClient;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly IHaipServiceClient? _haipClient;
    private readonly IPendingPresentationStore _pendingStore;
    private readonly IEnumerable<IPresentationConsumer> _consumers;
    private readonly IOptions<PresentationLifecycleOptions> _options;
    private readonly ILogger<PresentationLifecycleService> _logger;

    public PresentationLifecycleService(
        ITransactionBuilderService transactionBuilder,
        IWalletServiceClient walletClient,
        IValidatorServiceClient validatorClient,
        IPendingPresentationStore pendingStore,
        IEnumerable<IPresentationConsumer> consumers,
        IOptions<PresentationLifecycleOptions> options,
        ILogger<PresentationLifecycleService> logger,
        IHaipServiceClient? haipClient = null)
    {
        _transactionBuilder = transactionBuilder ?? throw new ArgumentNullException(nameof(transactionBuilder));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        _consumers = consumers ?? throw new ArgumentNullException(nameof(consumers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _haipClient = haipClient;
    }

    public async Task<PresentationInitiationResult> InitiateAsync(
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        CredentialRequirementModel credentialRequirement,
        string submitterWallet,
        string? delegationToken,
        IReadOnlyDictionary<string, object> draftPayload,
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(credentialRequirement);
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
        var requiredClaimNames = credentialRequirement.RequiredClaims?
            .Select(c => c.ClaimName)
            .ToList();
        var haipResult = await _haipClient.CreatePresentationRequestAsync(
            credentialRequirement.Type,
            requiredClaimNames,
            credentialRequirement.AcceptedIssuers?.ToList(),
            cancellationToken);

        // 2. Compute requirements digest (SHA-256 of canonical requirements JSON).
        var digest = ComputeRequirementsDigest(credentialRequirement);

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

        // Record the initiated tx id on the pending state so later
        // HandleOutcomeAsync / HandleAbandonmentAsync writes can set it as
        // previousTransactionId and preserve chain integrity on the register.
        await _pendingStore.StoreAsync(pending with { InitiatedTransactionId = built.TxId }, cancellationToken);

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

    public async Task<PresentationOutcomeResult> HandleOutcomeAsync(
        string consumerName,
        Guid presentationRequestId,
        object verifierPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);

        using var activity = ActivitySource.StartActivity("presentation.outcome");
        activity?.SetTag("presentation.request_id", presentationRequestId.ToString());
        activity?.SetTag("consumer", consumerName);

        var pending = await _pendingStore.GetAsync(presentationRequestId, cancellationToken);
        if (pending is null)
        {
            _logger.LogWarning(
                "PresentationOutcome callback for unknown or expired requestId {RequestId} from consumer {Consumer}",
                presentationRequestId, consumerName);
            throw new InvalidOperationException(
                $"No pending presentation found for requestId {presentationRequestId} (unknown or TTL-expired).");
        }

        if (!string.Equals(pending.ConsumerName, consumerName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Callback consumer '{consumerName}' does not match pending consumer '{pending.ConsumerName}'.");
        }

        // Dispatch to the consumer's verifier. The consumer implementation returns a
        // PresentationOutcome; this service owns transaction writing and routing.
        var consumer = _consumers.FirstOrDefault(c =>
            string.Equals(c.ConsumerName, consumerName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No IPresentationConsumer registered with name '{consumerName}'.");

        var context = new PresentationInitiationContext(
            PresentationRequestId: pending.PresentationRequestId,
            InstanceId: pending.InstanceId,
            ActionId: pending.ActionId,
            RegisterId: pending.RegisterId,
            BlueprintId: pending.BlueprintId,
            SubmitterWallet: pending.SubmitterWallet,
            RequirementsDigest: Convert.FromHexString(pending.CredentialRequirementDigestHex),
            InitiatedAt: pending.CreatedAt);

        var outcome = await consumer.VerifyAsync(context, verifierPayload, cancellationToken);

        // Two-level idempotency guard (research R6):
        //   - If outcome sentinel is "abandoned" we allow late outcome write → "abandoned+outcome"
        //   - If outcome sentinel is already "success" or "decline" we dedupe → return idempotent reply
        //   - Otherwise SET NX "outcome-pending-write" to claim the writer slot.
        var existingSentinel = await _pendingStore.GetOutcomeSentinelAsync(presentationRequestId, cancellationToken);
        var isLateAfterAbandonment = string.Equals(existingSentinel, "abandoned", StringComparison.Ordinal);

        if (!isLateAfterAbandonment &&
            (string.Equals(existingSentinel, "success", StringComparison.Ordinal) ||
             string.Equals(existingSentinel, "decline", StringComparison.Ordinal) ||
             string.Equals(existingSentinel, "abandoned+outcome", StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "Idempotent replay of outcome callback for requestId {RequestId}: sentinel already {Sentinel}",
                presentationRequestId, existingSentinel);
            return new PresentationOutcomeResult(
                Kind: outcome.Kind,
                OutcomeTransactionId: string.Empty,
                IsIdempotentReplay: true,
                IsLateAfterAbandonment: false);
        }

        if (!isLateAfterAbandonment)
        {
            var claimed = await _pendingStore.TryClaimOutcomeSentinelAsync(
                presentationRequestId, "outcome-pending-write",
                pending.ValidityWindowSeconds, cancellationToken);
            if (!claimed)
            {
                // Lost the race to a concurrent outcome call; treat as replay.
                _logger.LogInformation(
                    "Lost outcome sentinel race for requestId {RequestId}; deduplicated",
                    presentationRequestId);
                return new PresentationOutcomeResult(
                    Kind: outcome.Kind,
                    OutcomeTransactionId: string.Empty,
                    IsIdempotentReplay: true,
                    IsLateAfterAbandonment: false);
            }
        }

        // Build the outcome transaction.
        var outcomeDetailLevel = pending.OutcomeDetailLevel;
        IReadOnlyDictionary<string, object>? diagnosticsToWrite =
            string.Equals(outcomeDetailLevel, "verbose", StringComparison.OrdinalIgnoreCase)
                ? outcome.VerifierDiagnostics
                : null;

        var draftPayload = DeserializeDraftPayload(pending.DraftPayloadJson, presentationRequestId);

        // Placeholder blueprint/instance/action — BuildPresentationOutcomeAsync only
        // reads Id/Title and instance.RegisterId from them, so a lightweight shim
        // avoids a round-trip to storage for US2. Later stories (US3 retry, US4
        // abandonment) will materialise a real instance lookup here.
        var blueprintShim = new BlueprintModel
        {
            Id = pending.BlueprintId,
            Title = "outcome",
            Description = "outcome-shim",
            Version = 1,
            Participants = [],
            Actions = []
        };
        var instanceShim = new Instance
        {
            Id = pending.InstanceId.ToString(),
            BlueprintId = pending.BlueprintId,
            BlueprintVersion = 1,
            RegisterId = pending.RegisterId,
            TenantId = "outcome-shim"
        };
        var actionShim = new ActionModel { Id = pending.ActionId, BlueprintId = pending.BlueprintId };

        var built = await _transactionBuilder.BuildPresentationOutcomeAsync(
            blueprintShim, instanceShim, actionShim,
            presentationRequestId: presentationRequestId,
            consumerName: consumerName,
            submitterWallet: pending.SubmitterWallet,
            outcomeKind: outcome.Kind == PresentationOutcomeKind.Success ? "success" : "decline",
            verifiedClaims: outcome.VerifiedClaims,
            declineReason: outcome.Reason?.ToString(),
            verifierDiagnostics: diagnosticsToWrite,
            presentationSubmissionHash: outcome.PresentationSubmissionHash,
            actionPayload: outcome.Kind == PresentationOutcomeKind.Success ? draftPayload : null,
            previousTransactionId: pending.InitiatedTransactionId,
            cancellationToken);

        // Sign + submit.
        var signResult = await _walletClient.SignTransactionAsync(
            pending.SubmitterWallet, built.SigningData,
            derivationPath: null, isPreHashed: false, cancellationToken);
        built.SenderWallet = pending.SubmitterWallet;
        built.Signature = signResult.Signature;

        var nextSeqNum = await _validatorClient.GetNextSequenceNumberAsync(
            pending.RegisterId, pending.SubmitterWallet, cancellationToken);
        var submission = built.ToTransactionSubmission(signResult, nextSeqNum);
        var validatorResult = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

        if (!validatorResult.Success)
        {
            _logger.LogError(
                "Validator rejected presentation-outcome transaction {TxId}: [{ErrorCode}] {ErrorMessage}",
                built.TxId, validatorResult.ErrorCode, validatorResult.ErrorMessage);
            throw new InvalidOperationException(
                $"Validator rejected presentation-outcome transaction {built.TxId}: " +
                $"[{validatorResult.ErrorCode}] {validatorResult.ErrorMessage}");
        }

        // Mark sentinel with the final kind.
        var finalSentinel = (isLateAfterAbandonment, outcome.Kind) switch
        {
            (true, _) => "abandoned+outcome",
            (false, PresentationOutcomeKind.Success) => "success",
            (false, PresentationOutcomeKind.Decline) => "decline",
            _ => "decline"
        };
        await _pendingStore.SetOutcomeSentinelAsync(
            presentationRequestId, finalSentinel,
            pending.ValidityWindowSeconds, cancellationToken);

        _logger.LogInformation(
            "PresentationOutcome tx {TxId} written for requestId {RequestId} kind={Kind} sentinel={Sentinel}",
            built.TxId, presentationRequestId, outcome.Kind, finalSentinel);
        activity?.SetTag("outcome.kind", outcome.Kind.ToString());
        activity?.SetTag("tx.id", built.TxId);

        return new PresentationOutcomeResult(
            Kind: outcome.Kind,
            OutcomeTransactionId: built.TxId,
            IsIdempotentReplay: false,
            IsLateAfterAbandonment: isLateAfterAbandonment);
    }

    private IReadOnlyDictionary<string, object>? DeserializeDraftPayload(string? json, Guid requestId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch (JsonException ex)
        {
            // Partial-write or forward-incompatible change to the payload schema —
            // log loudly so operators can investigate. Returning null here means
            // a success-outcome tx will land with no actionPayload (FR-015
            // downstream routing still gets verifiedClaims).
            _logger.LogError(ex,
                "Failed to deserialise draftPayload for presentation {RequestId}; outcome will write without action payload. Raw JSON prefix: {Prefix}",
                requestId,
                json.Length > 256 ? json[..256] : json);
            return null;
        }
    }

    public async Task HandleAbandonmentAsync(
        Guid presentationRequestId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("presentation.abandoned");
        activity?.SetTag("presentation.request_id", presentationRequestId.ToString());

        var pending = await _pendingStore.GetAsync(presentationRequestId, cancellationToken);
        if (pending is null)
        {
            _logger.LogDebug(
                "AbandonmentSweeper no-op: requestId {RequestId} already cleaned up or unknown",
                presentationRequestId);
            return;
        }

        // Opt-in gate: only blueprints that asked for abandonment recording get a tx.
        if (!pending.RecordAbandonment)
        {
            _logger.LogDebug(
                "AbandonmentSweeper skip: requestId {RequestId} blueprint opted out of recordAbandonment",
                presentationRequestId);
            // Still delete the pending hash — the attempt simply evaporates.
            await _pendingStore.DeleteAsync(presentationRequestId, cancellationToken);
            return;
        }

        // Outcome-sentinel guard: if the outcome already resolved, don't write abandonment.
        var existingSentinel = await _pendingStore.GetOutcomeSentinelAsync(presentationRequestId, cancellationToken);
        if (existingSentinel is "success" or "decline"
                            or "outcome-pending-write" or "abandoned" or "abandoned+outcome")
        {
            _logger.LogDebug(
                "AbandonmentSweeper skip: requestId {RequestId} sentinel {Sentinel} — outcome already resolved",
                presentationRequestId, existingSentinel);
            return;
        }

        // Claim the sentinel as "abandoned" (first-writer-wins) so a concurrent
        // outcome callback takes the late-after-abandonment path instead.
        var claimed = await _pendingStore.TryClaimOutcomeSentinelAsync(
            presentationRequestId, "abandoned",
            pending.ValidityWindowSeconds, cancellationToken);
        if (!claimed)
        {
            _logger.LogDebug(
                "AbandonmentSweeper skip: requestId {RequestId} lost SET NX race to an outcome writer",
                presentationRequestId);
            return;
        }

        // Build + sign + submit the abandonment tx.
        var blueprintShim = new BlueprintModel
        {
            Id = pending.BlueprintId, Title = "abandonment", Description = "abandonment-shim",
            Version = 1, Participants = [], Actions = []
        };
        var instanceShim = new Instance
        {
            Id = pending.InstanceId.ToString(), BlueprintId = pending.BlueprintId,
            BlueprintVersion = 1, RegisterId = pending.RegisterId, TenantId = "abandonment-shim"
        };
        var actionShim = new ActionModel { Id = pending.ActionId, BlueprintId = pending.BlueprintId };

        var built = await _transactionBuilder.BuildPresentationAbandonedAsync(
            blueprintShim, instanceShim, actionShim,
            presentationRequestId: presentationRequestId,
            consumerName: pending.ConsumerName,
            submitterWallet: pending.SubmitterWallet,
            validityWindowSeconds: pending.ValidityWindowSeconds,
            previousTransactionId: pending.InitiatedTransactionId,
            cancellationToken);

        var signResult = await _walletClient.SignTransactionAsync(
            pending.SubmitterWallet, built.SigningData,
            derivationPath: null, isPreHashed: false, cancellationToken);
        built.SenderWallet = pending.SubmitterWallet;
        built.Signature = signResult.Signature;

        var nextSeqNum = await _validatorClient.GetNextSequenceNumberAsync(
            pending.RegisterId, pending.SubmitterWallet, cancellationToken);
        var submission = built.ToTransactionSubmission(signResult, nextSeqNum);
        var validatorResult = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

        if (!validatorResult.Success)
        {
            // Roll back the sentinel claim so a later outcome callback isn't
            // mistakenly treated as late-after-abandonment when there's no
            // abandonment tx on the register. Without this, HandleOutcomeAsync
            // would see sentinel="abandoned" and write "abandoned+outcome"
            // semantics even though only the outcome tx actually exists.
            try
            {
                await _pendingStore.DeleteOutcomeSentinelAsync(presentationRequestId, cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx,
                    "Failed to roll back sentinel claim after validator rejection for requestId {RequestId}; " +
                    "manual intervention may be required if a late outcome arrives",
                    presentationRequestId);
            }
            _logger.LogError(
                "Validator rejected abandonment tx {TxId} for requestId {RequestId}: [{Code}] {Msg}",
                built.TxId, presentationRequestId, validatorResult.ErrorCode, validatorResult.ErrorMessage);
            return;
        }

        _logger.LogInformation(
            "PresentationAbandoned tx {TxId} written for requestId {RequestId} consumer {Consumer}",
            built.TxId, presentationRequestId, pending.ConsumerName);
        activity?.SetTag("tx.id", built.TxId);
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
