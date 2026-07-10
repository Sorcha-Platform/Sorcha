// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.ServiceClients.Haip;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using CredentialRequirementModel = Sorcha.Blueprint.Models.Credentials.CredentialRequirement;
using PresentationSourceEnum = Sorcha.Blueprint.Models.Credentials.PresentationSource;
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
    private readonly IRegisterServiceClient? _registerClient;
    private readonly IPresentationSealCoordinator? _sealCoordinator;
    private readonly IHaipServiceClient? _haipClient;
    private readonly IPendingPresentationStore _pendingStore;
    private readonly IClaimsFetchTokenStore? _claimsFetchTokenStore;
    private readonly IDisclosedClaimsStore? _disclosedClaimsStore;
    private readonly IHubContext<BlueprintHub, IBlueprintHubClient>? _hubContext;
    private readonly IEnumerable<IPresentationConsumer> _consumers;
    private readonly IOptions<PresentationLifecycleOptions> _options;
    private readonly PresentationLifecycleMetrics? _metrics;
    private readonly IClock _clock;
    private readonly IServiceProvider? _serviceProvider;
    private readonly Sorcha.ServiceClients.OrgDidDocument.IOrgDidDocumentClient? _orgDidClient;
    private readonly IRequestObjectStore? _requestObjectStore;
    private readonly ILogger<PresentationLifecycleService> _logger;

    /// <summary>Constructor — DI-friendly.</summary>
    public PresentationLifecycleService(
        ITransactionBuilderService transactionBuilder,
        IWalletServiceClient walletClient,
        IValidatorServiceClient validatorClient,
        IPendingPresentationStore pendingStore,
        IEnumerable<IPresentationConsumer> consumers,
        IOptions<PresentationLifecycleOptions> options,
        ILogger<PresentationLifecycleService> logger,
        IHaipServiceClient? haipClient = null,
        PresentationLifecycleMetrics? metrics = null,
        IClock? clock = null,
        IServiceProvider? serviceProvider = null,
        IRegisterServiceClient? registerClient = null,
        IPresentationSealCoordinator? sealCoordinator = null,
        IClaimsFetchTokenStore? claimsFetchTokenStore = null,
        IDisclosedClaimsStore? disclosedClaimsStore = null,
        IHubContext<BlueprintHub, IBlueprintHubClient>? hubContext = null,
        Sorcha.ServiceClients.OrgDidDocument.IOrgDidDocumentClient? orgDidClient = null,
        IRequestObjectStore? requestObjectStore = null)
    {
        _transactionBuilder = transactionBuilder ?? throw new ArgumentNullException(nameof(transactionBuilder));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        _consumers = consumers ?? throw new ArgumentNullException(nameof(consumers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _haipClient = haipClient;
        _metrics = metrics;
        _clock = clock ?? new SystemClock();
        _serviceProvider = serviceProvider;
        _registerClient = registerClient;
        _sealCoordinator = sealCoordinator;
        _claimsFetchTokenStore = claimsFetchTokenStore;
        _disclosedClaimsStore = disclosedClaimsStore;
        _hubContext = hubContext;
        _orgDidClient = orgDidClient;
        _requestObjectStore = requestObjectStore;
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

        var config = ResolveConfig(blueprint);
        var validityWindow = config.PresentationValidityWindowSeconds
            ?? _options.Value.DefaultValidityWindowSeconds;

        // Feature 127 — dispatch by PresentationSource. HAIP keeps the legacy
        // hardcoded path; SorchaWallet uses the new BuildInitiationAsync
        // extension contract and mints a single-use ClaimsFetchToken for the
        // council-page autofill path. Default-to-HAIP for back-compat with
        // existing blueprints that don't explicitly set PresentationSource.
        var consumerName = credentialRequirement.PresentationSource switch
        {
            PresentationSourceEnum.SorchaWallet => SorchaWalletPresentationConsumer.ConsumerNameValue,
            _ => "haip"
        };

        using var activity = ActivitySource.StartActivity("presentation.initiated");
        activity?.SetTag("instance.id", instance.Id);
        activity?.SetTag("action.id", action.Id);
        activity?.SetTag("register.id", instance.RegisterId);
        activity?.SetTag("consumer", consumerName);

        // 1. Build the wallet-facing artifact (request URI + requestId + expiry).
        //    HAIP delegates to its external service; non-HAIP consumers use the
        //    F127 BuildInitiationAsync extension on IPresentationConsumer.
        InitiationDescriptor descriptor;
        if (consumerName == "haip")
        {
            if (_haipClient is null)
            {
                throw new InvalidOperationException(
                    "HAIP consumer client is not registered; cannot initiate HAIP presentation.");
            }

            var requiredClaimNames = credentialRequirement.RequiredClaims?
                .Select(c => c.ClaimName)
                .ToList();
            // Feature 135: the issuer allowlist for the HAIP presentation request is sourced
            // from the requirement's trust policy (did-allowlist sources).
            var allowedIssuers = Sorcha.Blueprint.Models.Credentials.TrustPolicyExtensions
                .AllowedIssuerDids(credentialRequirement.TrustPolicy);
            var haipResult = await _haipClient.CreatePresentationRequestAsync(
                credentialRequirement.Type,
                requiredClaimNames,
                allowedIssuers.Count > 0 ? allowedIssuers.ToList() : null,
                cancellationToken);

            descriptor = new InitiationDescriptor(
                PresentationRequestId: haipResult.RequestId,
                AuthorizationRequestUri: haipResult.AuthorizationRequestUri,
                RequestUri: haipResult.RequestUri,
                Nonce: haipResult.Nonce,
                ExpiresAt: haipResult.ExpiresAt);
        }
        else
        {
            // F127 path. Lifecycle service owns the requestId for non-HAIP
            // consumers; the consumer's BuildInitiationAsync produces the URI
            // shape it wants the wallet to see.
            var requestId = Guid.NewGuid();
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(validityWindow);
            var consumer = _consumers.FirstOrDefault(c =>
                string.Equals(c.ConsumerName, consumerName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"No IPresentationConsumer registered with name '{consumerName}'.");

            var digestPreview = ComputeRequirementsDigest(credentialRequirement);

            // Spec 5 — resolve the verifier (council org) DID so the consumer can
            // emit a real client_id instead of did:sorcha:org:UNKNOWN. Best-effort:
            // a null result (no org id, unpublished DID doc, or transport failure)
            // leaves the consumer's placeholder fallback intact and never blocks the gate.
            string? verifierClientId = null;
            if (_orgDidClient is not null
                && !string.IsNullOrWhiteSpace(blueprint.OrganizationId)
                && Guid.TryParse(blueprint.OrganizationId, out var orgGuid))
            {
                verifierClientId = await _orgDidClient.ResolveCanonicalDidAsync(orgGuid, cancellationToken);
                if (verifierClientId is null)
                {
                    _logger.LogDebug(
                        "Verifier DID unresolved for org {OrgId} (blueprint {BlueprintId}); " +
                        "consumer will use placeholder client_id.",
                        blueprint.OrganizationId, blueprint.Id);
                }
            }

            var ctx = new PresentationInitiationContext(
                PresentationRequestId: requestId,
                InstanceId: Guid.Parse(instance.Id),
                ActionId: action.Id,
                RegisterId: instance.RegisterId,
                BlueprintId: blueprint.Id,
                SubmitterWallet: submitterWallet,
                RequirementsDigest: digestPreview,
                InitiatedAt: DateTimeOffset.UtcNow,
                VerifierClientId: verifierClientId,
                // Feature 181 (T014) — consumers that serve a request object need the ask
                // itself (not just its digest) to emit dcql_query, plus the public base
                // URL for cross-device request_uri/response_uri reachability.
                CredentialType: credentialRequirement.Type,
                RequiredClaimNames: credentialRequirement.RequiredClaims?
                    .Select(c => c.ClaimName).ToList(),
                PublicBaseUrl: _options.Value.PublicBaseUrl);

            var consumerDescriptor = await consumer.BuildInitiationAsync(ctx, cancellationToken);

            // Feature 181 (T014) — stash the served request object for the anonymous
            // GET /api/presentations/{id}/request-object route. TTL = validity window.
            if (consumerDescriptor.RequestObjectJwt is not null && _requestObjectStore is not null)
            {
                await _requestObjectStore.StoreAsync(
                    requestId,
                    consumerDescriptor.RequestObjectJwt,
                    TimeSpan.FromSeconds(validityWindow),
                    cancellationToken);
            }

            descriptor = new InitiationDescriptor(
                PresentationRequestId: requestId,
                AuthorizationRequestUri: consumerDescriptor.AuthorizationRequestUri,
                RequestUri: consumerDescriptor.RequestUri,
                Nonce: consumerDescriptor.Nonce,
                ExpiresAt: expiresAt);
        }

        // 2. Compute requirements digest (SHA-256 of canonical requirements JSON).
        var digest = ComputeRequirementsDigest(credentialRequirement);

        // 3. Store pending state in Redis so the callback and abandonment paths can
        //    reconstitute the draft action payload and blueprint config.
        var pending = new PendingPresentation
        {
            PresentationRequestId = descriptor.PresentationRequestId,
            InstanceId = Guid.Parse(instance.Id),
            ActionId = action.Id,
            RegisterId = instance.RegisterId,
            BlueprintId = blueprint.Id,
            SubmitterWallet = submitterWallet,
            ConsumerName = consumerName,
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
            presentationRequestId: descriptor.PresentationRequestId,
            consumerName: consumerName,
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
            await _pendingStore.DeleteAsync(descriptor.PresentationRequestId, cancellationToken);
            throw new InvalidOperationException(
                $"Validator rejected presentation-initiated transaction {built.TxId}: " +
                $"[{validatorResult.ErrorCode}] {validatorResult.ErrorMessage}");
        }

        // Record the initiated tx id on the pending state so later
        // HandleOutcomeAsync / HandleAbandonmentAsync writes can set it as
        // previousTransactionId and preserve chain integrity on the register.
        await _pendingStore.StoreAsync(pending with { InitiatedTransactionId = built.TxId }, cancellationToken);

        // Feature 127 — mint a single-use ClaimsFetchToken for consumers that
        // produce council-page-readable disclosed claims (Sorcha wallet today).
        // HAIP outcomes are decoded only on the register side; HAIP callers get
        // no token. Token TTL = validity window so it expires alongside the
        // pending presentation if the wallet never returns.
        string? claimsFetchToken = null;
        if (consumerName == SorchaWalletPresentationConsumer.ConsumerNameValue &&
            _claimsFetchTokenStore is not null)
        {
            claimsFetchToken = GenerateClaimsFetchToken();
            await _claimsFetchTokenStore.StoreAsync(
                claimsFetchToken,
                descriptor.PresentationRequestId,
                TimeSpan.FromSeconds(validityWindow),
                cancellationToken);
        }

        _logger.LogInformation(
            "PresentationInitiated tx {TxId} submitted for instance {InstanceId} action {ActionId} requestId {RequestId} consumer={Consumer}",
            built.TxId, instance.Id, action.Id, descriptor.PresentationRequestId, consumerName);
        activity?.SetTag("presentation.request_id", descriptor.PresentationRequestId.ToString());
        activity?.SetTag("tx.id", built.TxId);
        _metrics?.RecordInitiated(pending.ConsumerName);

        return new PresentationInitiationResult(
            PresentationRequestId: descriptor.PresentationRequestId,
            AuthorizationRequestUri: descriptor.AuthorizationRequestUri,
            RequestUri: descriptor.RequestUri,
            Nonce: descriptor.Nonce,
            ExpiresAt: descriptor.ExpiresAt,
            InitiatedTransactionId: built.TxId,
            ClaimsFetchToken: claimsFetchToken);
    }

    /// <summary>
    /// Internal value carried between the consumer-specific initiation branch
    /// and the consumer-agnostic transaction-write / pending-store body.
    /// </summary>
    private sealed record InitiationDescriptor(
        Guid PresentationRequestId,
        string AuthorizationRequestUri,
        string? RequestUri,
        string? Nonce,
        DateTimeOffset ExpiresAt);

    /// <summary>
    /// Feature 127 — high-entropy URL-safe token bound to a single presentation
    /// request. Mints 24 raw bytes (192 bits) of entropy, URL-safe base64
    /// encoded. Single-use enforcement is the store's job.
    /// </summary>
    private static string GenerateClaimsFetchToken()
    {
        var raw = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(raw)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
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
             string.Equals(existingSentinel, "abandoned+outcome", StringComparison.Ordinal) ||
             // Feature 119 — writer-claimed-but-deferred state, deduplicate replays just like outcome-pending-write.
             string.Equals(existingSentinel, "outcome-pending-seal", StringComparison.Ordinal)))
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

        // Feature 145 US6 — on a SUCCESSFUL outcome, attach a sender-signed RoutingDecision to the
        // outcome tx's clear metadata so the InstanceProjector advances the instance when this tx
        // seals (the imperative CompleteAfterPresentationAsync advance is retired). Decline /
        // abandonment carry no decision, so the projector leaves the gated action current for retry
        // (InstanceProjectionResolver skips a presentation-lifecycle tx with no decision). The
        // decision rides through ToTransactionSubmission's whitelist (key "routingDecision") to the
        // validator (VAL_ROUTING_*) and the sealed docket. Attaching metadata here does NOT change
        // the txId/signature (those are over TransactionData/SigningData, not Metadata).
        // The routing-decision builder IS ActionExecutionService (it implements IPresentationRoutingDecisionBuilder,
        // registered via a forwarding factory). Resolve it LAZILY here instead of via the constructor: a ctor-time
        // dependency forms a circular scoped DI graph — ActionExecutionService → IPresentationLifecycleService →
        // IPresentationRoutingDecisionBuilder → (factory) ActionExecutionService — which MEDI cannot detect through
        // the factory lambda and which DEADLOCKS when the /execute endpoint resolves ActionExecutionService (the
        // F145 504-hang root cause). By the time this method runs, this PresentationLifecycleService is already
        // cached in the request scope, so resolving ActionExecutionService here returns without re-entering its
        // construction — no cycle.
        var routingDecisionBuilder = _serviceProvider?.GetService<IPresentationRoutingDecisionBuilder>();
        if (outcome.Kind == PresentationOutcomeKind.Success && routingDecisionBuilder is not null)
        {
            var routingDecision = await routingDecisionBuilder.BuildForPresentationOutcomeAsync(
                pending.InstanceId.ToString(), pending.ActionId, draftPayload,
                pending.SubmitterWallet, cancellationToken);
            if (routingDecision is not null)
            {
                built.Metadata["routingDecision"] = System.Text.Json.JsonSerializer.Serialize(
                    routingDecision, Sorcha.Register.Models.RegisterSerializationOptions.Canonical);
            }
            else
            {
                _logger.LogWarning(
                    "US6: no routing decision built for successful presentation outcome (instance {InstanceId} action {ActionId}); " +
                    "the projector will not advance this instance — verify the action is current",
                    pending.InstanceId, pending.ActionId);
            }
        }

        var nextSeqNum = await _validatorClient.GetNextSequenceNumberAsync(
            pending.RegisterId, pending.SubmitterWallet, cancellationToken);
        var submission = built.ToTransactionSubmission(signResult, nextSeqNum);

        // Feature 119 — seal-aware ordering. The outcome tx's previousTransactionId
        // points at the initiated tx. If that predecessor has not yet sealed, submitting
        // here races the validator's chain check (VAL_CHAIN_001). Defer to the
        // coordinator and return a "pending-seal" reply; the seal subscriber drains
        // the queue after the predecessor's transaction:confirmed event arrives.
        // Late-after-abandonment branch keeps the inline path: by sweeper-fire time
        // the initiated tx has long since sealed.
        if (!isLateAfterAbandonment &&
            _registerClient is not null &&
            _sealCoordinator is not null &&
            !string.IsNullOrEmpty(pending.InitiatedTransactionId))
        {
            var predecessorSealed = await IsPredecessorSealedAsync(
                pending.RegisterId, pending.InitiatedTransactionId!, cancellationToken);
            if (!predecessorSealed)
            {
                var targetSentinel = (outcome.Kind == PresentationOutcomeKind.Success) ? "success" : "decline";
                await _pendingStore.SetOutcomeSentinelAsync(
                    presentationRequestId, "outcome-pending-seal",
                    pending.ValidityWindowSeconds, cancellationToken);
                await _sealCoordinator.EnqueueSubmissionAsync(new SealAwaitingSubmission(
                    PresentationRequestId: presentationRequestId,
                    PredecessorTxId: pending.InitiatedTransactionId!,
                    Site: SealAwaitingSubmissionSite.Outcome,
                    Submission: submission,
                    TargetSentinelOnSuccess: targetSentinel,
                    ValidityWindowSeconds: pending.ValidityWindowSeconds,
                    EnqueuedAt: _clock.UtcNow,
                    TraceContext: System.Diagnostics.Activity.Current?.Id ?? string.Empty),
                    cancellationToken);

                // Feature 145 US6 — no separate advancement enqueue. The deferred submission carries
                // the signed RoutingDecision (attached above for a successful outcome), so when it
                // drains and seals, every node's InstanceProjector folds it and advances the instance.
                // The imperative CompleteAfterPresentationAsync advance is retired.

                _logger.LogInformation(
                    "Presentation outcome deferred (predecessor {PredecessorTxId} not sealed) requestId={RequestId} txId={TxId} kind={Kind}",
                    pending.InitiatedTransactionId, presentationRequestId, built.TxId, outcome.Kind);

                _metrics?.RecordOutcome(
                    consumer: consumerName,
                    kind: outcome.Kind.ToString().ToLowerInvariant(),
                    reason: outcome.Reason?.ToString());

                return new PresentationOutcomeResult(
                    Kind: outcome.Kind,
                    OutcomeTransactionId: built.TxId,
                    IsIdempotentReplay: false,
                    IsLateAfterAbandonment: false);
            }
        }

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
            "PresentationOutcomeWritten tx {TxId} requestId {RequestId} kind={Kind} sentinel={Sentinel} lateAfterAbandonment={Late}",
            built.TxId, presentationRequestId, outcome.Kind, finalSentinel, isLateAfterAbandonment);
        activity?.SetTag("outcome.kind", outcome.Kind.ToString());
        activity?.SetTag("tx.id", built.TxId);

        // Feature 127 — on success, stash disclosed claims in Redis so the
        // council page's claims-fetch endpoint can return them in plaintext
        // without re-decrypting the register record. TTL = remaining validity
        // window. MUST happen BEFORE the hub publish so the council page can
        // race-safely fetch claims the moment it receives the signal.
        if (outcome.Kind == PresentationOutcomeKind.Success &&
            outcome.VerifiedClaims is not null &&
            _disclosedClaimsStore is not null)
        {
            var remainingTtl = pending.CreatedAt
                .AddSeconds(pending.ValidityWindowSeconds) - _clock.UtcNow;
            // Floor at 10 s so even a near-expiry outcome leaves some room for
            // the council page to fetch.
            if (remainingTtl < TimeSpan.FromSeconds(10))
            {
                remainingTtl = TimeSpan.FromSeconds(10);
            }
            try
            {
                await _disclosedClaimsStore.StoreAsync(
                    presentationRequestId,
                    outcome.VerifiedClaims,
                    remainingTtl,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to stash disclosed claims for requestId {RequestId}; council-page autofill will fail back to manual entry",
                    presentationRequestId);
            }
        }

        // Feature 127 — thin-signal hub publish so council pages subscribed to
        // BlueprintHubGroups.PresentationNonce learn of the outcome with low
        // latency. The signal carries the requestId only; the council page
        // fetches lifecycle state (kind) via F111's status endpoint and, on
        // success, disclosed claims via the F127 claims-fetch endpoint.
        // Try/log/swallow — publishing must never fail the outcome write.
        // TODO(seal-drain): mirror this publish from PresentationSealSubscriber
        // when an F119-deferred outcome eventually writes; for the typical
        // inline path (no F119 deferral), this single publish is sufficient.
        if (_hubContext is not null)
        {
            var publishStart = _clock.UtcNow;
            try
            {
                await _hubContext.Clients
                    .Group(BlueprintHubGroups.PresentationNonce(presentationRequestId))
                    .PresentationOutcomeReady(presentationRequestId.ToString("N"));
                _metrics?.RecordOutcomeSignalLatency(
                    kind: outcome.Kind.ToString().ToLowerInvariant(),
                    seconds: (_clock.UtcNow - publishStart).TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to publish PresentationOutcomeReady for requestId {RequestId}; non-fatal",
                    presentationRequestId);
            }
        }

        _metrics?.RecordOutcome(
            consumer: consumerName,
            kind: outcome.Kind.ToString().ToLowerInvariant(),
            reason: outcome.Reason?.ToString());
        _metrics?.RecordDuration(
            consumer: consumerName,
            kind: outcome.Kind.ToString().ToLowerInvariant(),
            seconds: (_clock.UtcNow - pending.CreatedAt).TotalSeconds);

        // Feature 145 US6 — FR-015 advancement is now the InstanceProjector's job. The inline outcome
        // tx submitted above carries the signed RoutingDecision (for a successful outcome), so when it
        // seals, every node folds it and advances the instance; the ReactionDispatcher fires the
        // action-available / workflow-completed notifications post-fold. This retires the imperative
        // CompleteAfterPresentationAsync advance (both the F119 advancement-queue path and the legacy
        // Task.Run fallback) and the VAL_BP_003 / state-reconstruction race that motivated F119's
        // advancement deferral — the seal-ordered projection guarantees ordering. The F119 outcome-tx
        // SUBMISSION deferral (EnqueueSubmissionAsync, above) is retained for predecessor-seal ordering.
        // Decline / abandonment carry no decision, so the projector leaves the action current.

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

        // Feature 119 — seal-aware ordering. The abandonment tx's
        // previousTransactionId points at the initiated tx. Race window is much
        // smaller than the outcome path (sweeper fires after validity-window
        // expiry), but the same defect exists. Defer to the coordinator if the
        // predecessor has not yet sealed.
        if (_registerClient is not null &&
            _sealCoordinator is not null &&
            !string.IsNullOrEmpty(pending.InitiatedTransactionId))
        {
            var predecessorSealed = await IsPredecessorSealedAsync(
                pending.RegisterId, pending.InitiatedTransactionId!, cancellationToken);
            if (!predecessorSealed)
            {
                await _sealCoordinator.EnqueueSubmissionAsync(new SealAwaitingSubmission(
                    PresentationRequestId: presentationRequestId,
                    PredecessorTxId: pending.InitiatedTransactionId!,
                    Site: SealAwaitingSubmissionSite.Abandonment,
                    Submission: submission,
                    TargetSentinelOnSuccess: "abandoned",
                    ValidityWindowSeconds: pending.ValidityWindowSeconds,
                    EnqueuedAt: _clock.UtcNow,
                    TraceContext: System.Diagnostics.Activity.Current?.Id ?? string.Empty),
                    cancellationToken);

                _logger.LogInformation(
                    "Abandonment tx deferred (predecessor {PredecessorTxId} not sealed) requestId={RequestId} txId={TxId}",
                    pending.InitiatedTransactionId, presentationRequestId, built.TxId);
                return;
            }
        }

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
            "PresentationAbandoned tx {TxId} requestId {RequestId} consumer {Consumer} blueprint {BlueprintId}",
            built.TxId, presentationRequestId, pending.ConsumerName, pending.BlueprintId);
        activity?.SetTag("tx.id", built.TxId);
        _metrics?.RecordAbandoned(pending.ConsumerName, pending.BlueprintId);
    }

    /// <summary>
    /// Feature 119 — check whether a predecessor transaction has already sealed in the
    /// register. Returns false on any failure (treating "uncertain" as "not sealed" so
    /// we defer rather than risk VAL_CHAIN_001).
    /// </summary>
    private async Task<bool> IsPredecessorSealedAsync(string registerId, string txId, CancellationToken ct)
    {
        if (_registerClient is null) return true;
        try
        {
            var tx = await _registerClient.GetTransactionAsync(registerId, txId, ct);
            return tx is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Predecessor seal-check threw for tx {TxId} on register {RegisterId}; deferring submission",
                txId, registerId);
            return false;
        }
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
        // Feature 135: the audited issuer allowlist is sourced from the trust policy.
        var allowedIssuers = Sorcha.Blueprint.Models.Credentials.TrustPolicyExtensions
            .AllowedIssuerDids(requirement.TrustPolicy);
        var canonical = new
        {
            type = requirement.Type,
            acceptedIssuers = allowedIssuers.Count > 0
                ? allowedIssuers.OrderBy(x => x, StringComparer.Ordinal).ToArray()
                : null,
            requiredClaims = requirement.RequiredClaims?
                .OrderBy(c => c.ClaimName, StringComparer.Ordinal)
                .Select(c => c.ClaimName)
                .ToArray()
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return SHA256.HashData(json);
    }
}
