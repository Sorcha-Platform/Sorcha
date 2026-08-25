// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.PlatformUserClaims;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Haip;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Schemas;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.Cryptography.Enums;
using Sorcha.Register.Models;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using EncryptionOpStatus = Sorcha.Blueprint.Service.Models.EncryptionOperationStatus;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Orchestrates workflow action execution.
/// Coordinates state reconstruction, validation, routing, transaction building, and notifications.
/// </summary>
public class ActionExecutionService : IActionExecutionService, IPresentationRoutingDecisionBuilder
{
    private readonly IActionResolverService _actionResolver;
    private readonly IStateReconstructionService _stateReconstruction;
    private readonly ITransactionBuilderService _transactionBuilder;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly IPeerServiceClient? _peerClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly IParticipantServiceClient _participantClient;
    private readonly INotificationService _notificationService;
    private readonly IInstanceStore _instanceStore;
    private readonly IExecutionEngine _executionEngine;
    private readonly IActionDisclosureResolver _actionDisclosureResolver;
    private readonly IJsonLogicEvaluator? _jsonLogicEvaluator;
    private readonly IPlatformUserClaimsClient? _platformUserClaims;
    private readonly ICredentialVerifier? _credentialVerifier;
    private readonly IStatusListManager? _statusListManager;
    private readonly IEncryptionPipelineService? _encryptionPipeline;
    private readonly IDisclosureGroupBuilder? _disclosureGroupBuilder;
    private readonly Channel<EncryptionWorkItem>? _encryptionChannel;
    private readonly IEncryptionOperationStore? _encryptionOperationStore;
    private readonly IHaipServiceClient? _haipClient;
    private readonly IPresentationLifecycleService? _presentationLifecycle;
    private readonly IPresentationRateLimiter? _presentationRateLimiter;
    private readonly PresentationLifecycleMetrics? _presentationMetrics;
    private readonly IActionStore _actionStore;
    private readonly IInstanceBindingCache? _bindingCache;
    private readonly TransactionConfirmationOptions _confirmationOptions;
    private readonly bool _credentialStatusEmbeddingEnabled;
    private readonly Configuration.WalletOwnershipSettings _walletOwnershipSettings;
    private readonly ILogger<ActionExecutionService> _logger;
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.Service.ActionExecution");

    /// <summary>
    /// Maximum actions per workflow instance (SEC-AUDIT 3.8). Prevents routing cycles from infinite loops.
    /// </summary>
    private const int MaxExecutionDepth = 1000;

    /// <summary>
    /// Stable claim-source prefix for a verified credential presentation's disclosed claims
    /// (Feature 174 / #1195 Phase 2, "one assurance, two bindings"). A
    /// <c>credentialIssuanceConfig.claimMappings</c> entry with
    /// <c>sourceField: "/presentedCredential/givenName"</c> resolves the <c>givenName</c> disclosed by
    /// the verified, issuer-signed presentation captured at the action's <c>credentialRequirements</c>
    /// gate — the mechanism by which a device-bound copy of a credential carries the assured claim set
    /// forward from the verified root presentation (design §4.1), NOT from client-supplied payload.
    /// <para>
    /// SECURITY: values under this prefix are AUTHORITATIVE. They come only from a verified presentation
    /// (the synchronous internal verifier this execution, or a prior gated action's reconstructed data)
    /// and always take precedence over the submitted payload. A client MUST NOT be able to override or
    /// spoof an identity claim by submitting a <c>presentedCredential</c> field — any payload-supplied
    /// value at this key is dropped and replaced with the verified source, fail-closed.
    /// </para>
    /// </summary>
    internal const string PresentedCredentialClaimSourceKey = "presentedCredential";

    /// <summary>Initialises a new instance of the <see cref="ActionExecutionService"/> class.</summary>
    public ActionExecutionService(
        IActionResolverService actionResolver,
        IStateReconstructionService stateReconstruction,
        ITransactionBuilderService transactionBuilder,
        IRegisterServiceClient registerClient,
        IValidatorServiceClient validatorClient,
        IWalletServiceClient walletClient,
        IParticipantServiceClient participantClient,
        INotificationService notificationService,
        IInstanceStore instanceStore,
        IActionStore actionStore,
        IExecutionEngine executionEngine,
        ILogger<ActionExecutionService> logger,
        IConfiguration configuration,
        ICredentialVerifier? credentialVerifier = null,
        IOptions<TransactionConfirmationOptions>? confirmationOptions = null,
        IStatusListManager? statusListManager = null,
        IEncryptionPipelineService? encryptionPipeline = null,
        IDisclosureGroupBuilder? disclosureGroupBuilder = null,
        Channel<EncryptionWorkItem>? encryptionChannel = null,
        IEncryptionOperationStore? encryptionOperationStore = null,
        IHaipServiceClient? haipClient = null,
        IInstanceBindingCache? bindingCache = null,
        IPeerServiceClient? peerClient = null,
        IPresentationLifecycleService? presentationLifecycle = null,
        IPresentationRateLimiter? presentationRateLimiter = null,
        PresentationLifecycleMetrics? presentationMetrics = null,
        IOptions<Configuration.WalletOwnershipSettings>? walletOwnershipSettings = null,
        IActionDisclosureResolver? actionDisclosureResolver = null,
        IJsonLogicEvaluator? jsonLogicEvaluator = null,
        IPlatformUserClaimsClient? platformUserClaims = null)
    {
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _stateReconstruction = stateReconstruction ?? throw new ArgumentNullException(nameof(stateReconstruction));
        _transactionBuilder = transactionBuilder ?? throw new ArgumentNullException(nameof(transactionBuilder));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _peerClient = peerClient;
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _participantClient = participantClient ?? throw new ArgumentNullException(nameof(participantClient));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _instanceStore = instanceStore ?? throw new ArgumentNullException(nameof(instanceStore));
        _actionStore = actionStore ?? throw new ArgumentNullException(nameof(actionStore));
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _credentialVerifier = credentialVerifier;
        _confirmationOptions = confirmationOptions?.Value ?? new TransactionConfirmationOptions();
        _statusListManager = statusListManager;
        _encryptionPipeline = encryptionPipeline;
        _disclosureGroupBuilder = disclosureGroupBuilder;
        _encryptionChannel = encryptionChannel;
        _encryptionOperationStore = encryptionOperationStore;
        _haipClient = haipClient;
        _presentationLifecycle = presentationLifecycle;
        _presentationRateLimiter = presentationRateLimiter;
        _presentationMetrics = presentationMetrics;
        _bindingCache = bindingCache;

        // Feature 093 US2: read the CredentialStatus:EnableEmbedding flag. When false,
        // ActionExecutionService skips the pre-signing status list allocation and
        // credentials are issued without the credentialStatus claim — matching pre-fix
        // behaviour for dev environments that do not run a status list manager.
        _credentialStatusEmbeddingEnabled =
            configuration?.GetValue<bool?>("CredentialStatus:EnableEmbedding") ?? true;

        _walletOwnershipSettings = walletOwnershipSettings?.Value
            ?? new Configuration.WalletOwnershipSettings();

        // Feature 176: disclosure resolution is now a shared authority (IActionDisclosureResolver) so the
        // execution path and the disclosed-data query endpoint use one implementation. Constructed with a
        // NullLogger fallback for direct (non-DI) test construction; the submit-side primitive needs only
        // the engine + register client (its read-side deps stay null here).
        _actionDisclosureResolver = actionDisclosureResolver
            ?? new ActionDisclosureResolver(
                executionEngine,
                registerClient,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionDisclosureResolver>.Instance);

        // Feature 176 / FR-004: gates credential issuance on the submitted decision so a rejected
        // application is never issued a credential. Optional — a null evaluator with no configured
        // issuanceCondition preserves the pre-existing always-issue behaviour.
        _jsonLogicEvaluator = jsonLogicEvaluator;
        _platformUserClaims = platformUserClaims;
    }

    /// <inheritdoc/>
    public async Task<ActionSubmissionResponse> ExecuteAsync(
        string instanceId,
        int actionId,
        ActionSubmissionRequest request,
        string delegationToken,
        ClaimsPrincipal? caller = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ExecuteAction");
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("action.id", actionId);

        _logger.LogInformation("Executing action {ActionId} for instance {InstanceId}", actionId, instanceId);

        // 1. Get the instance (before idempotency check, so we can include cycle context)
        var instance = await _instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException($"Instance {instanceId} not found");
        }

        // 1a. Cycle/depth guard — prevent infinite loops in cyclic blueprints (SEC-AUDIT 3.8)
        if (instance.CompletedActionCount >= MaxExecutionDepth)
        {
            _logger.LogWarning(
                "Instance {InstanceId} exceeded maximum execution depth ({MaxDepth}). Possible routing cycle in blueprint {BlueprintId}",
                instanceId, MaxExecutionDepth, instance.BlueprintId);
            throw new InvalidOperationException(
                $"Workflow instance has exceeded the maximum execution depth of {MaxExecutionDepth}. " +
                "This may indicate a routing cycle in the blueprint definition.");
        }

        // 1b. Replay protection — idempotency check
        // Include LastTransactionId in the key so cyclic workflows (where the same action
        // is executed multiple times) generate unique keys per cycle.
        var idempotencyKey = GenerateIdempotencyKey(instanceId, actionId, request.SenderWallet, instance.LastTransactionId);
        var existingTxHash = await _actionStore.GetByIdempotencyKeyAsync(idempotencyKey);
        if (existingTxHash != null)
        {
            _logger.LogWarning(
                "Duplicate submission detected for instance {InstanceId} action {ActionId}. Existing tx: {TxHash}",
                instanceId, actionId, existingTxHash);
            throw new InvalidOperationException(
                $"Duplicate submission. This action was already executed (transaction: {existingTxHash}).");
        }

        // 2. Get the blueprint
        var blueprint = await _actionResolver.GetBlueprintAsync(instance.BlueprintId, instance.BlueprintDefinitionTxId, cancellationToken);
        if (blueprint == null)
        {
            throw new InvalidOperationException($"Blueprint {instance.BlueprintId} not found");
        }

        // 3. Get the action definition
        var actionDef = _actionResolver.GetActionDefinition(blueprint, actionId.ToString());
        if (actionDef == null)
        {
            throw new InvalidOperationException($"Action {actionId} not found in blueprint {blueprint.Id}");
        }

        // 4. Validate the action can be executed (is it a current action?)
        if (!instance.CurrentActionIds.Contains(actionId) && !actionDef.IsStartingAction)
        {
            throw new InvalidOperationException($"Action {actionId} is not a current action for instance {instanceId}");
        }

        // 4b. Validate wallet ownership (SEC-006). Feature 103: an OPEN starting action — one whose
        // Sender participant carries no hardcoded wallet — accepts a walk-in submitter who has no
        // participant profile yet (they are late-bound at 4d below). SEC-006's fail-closed
        // missing-participant check must NOT block them (#911): F136 consumer tokens now carry org_id,
        // which would otherwise trip the check the open-participant flow exists to bypass. Wallet
        // ownership is still enforced by the Wallet Service at signing time, and participant binding
        // by the validator (VAL_BP_002) + the late-bind below.
        var senderParticipantDef = blueprint.Participants?
            .FirstOrDefault(p => string.Equals(p.Id, actionDef.Sender, StringComparison.OrdinalIgnoreCase));
        var isOpenStartingAction = actionDef.IsStartingAction
            && string.IsNullOrWhiteSpace(senderParticipantDef?.WalletAddress);
        await ValidateWalletOwnershipAsync(request.SenderWallet, caller, isOpenStartingAction, cancellationToken);

        // 4c. Validate sender matches action's designated participant role (SEC-AUDIT 3.1)
        // When a participant has a hardcoded wallet address in the blueprint, enforce strict matching.
        // When no wallet is hardcoded, log a warning — the Validator's blueprint conformance (VAL_BP_002)
        // provides the authoritative check at the chain level.
        if (!string.IsNullOrWhiteSpace(actionDef.Sender))
        {
            var senderParticipant = blueprint.Participants?.FirstOrDefault(p =>
                string.Equals(p.Id, actionDef.Sender, StringComparison.OrdinalIgnoreCase));

            if (senderParticipant != null && !string.IsNullOrWhiteSpace(senderParticipant.WalletAddress))
            {
                var isAuthorizedSender = string.Equals(
                    senderParticipant.WalletAddress, request.SenderWallet, StringComparison.OrdinalIgnoreCase);

                if (!isAuthorizedSender)
                {
                    _logger.LogWarning(
                        "Sender wallet {Wallet} does not match designated participant wallet {Expected} for action {ActionId} in instance {InstanceId}",
                        request.SenderWallet, senderParticipant.WalletAddress, actionId, instanceId);
                    throw new InvalidOperationException(
                        $"Wallet {request.SenderWallet} is not authorized to execute action {actionId}. " +
                        $"This action requires participant '{actionDef.Sender}' with wallet '{senderParticipant.WalletAddress}'.");
                }
            }
        }

        // 4c. Verify credential presentations against action requirements.
        //
        // Feature 174 / #1195 Phase 2 — the verified presentation's disclosed claims are the
        // authoritative source for any issuance claim mapped from /presentedCredential/* (see
        // PresentedCredentialClaimSourceKey). Captured here at the synchronous gate and threaded into
        // the issuance source document below (step 8a-bis) so device-copy identity claims come from the
        // verified, issuer-signed root presentation — never from client-supplied payload. Null when no
        // synchronous presentation was verified this execution (e.g. an async SorchaWallet gate, or no
        // credential requirement at all).
        Dictionary<string, object>? verifiedPresentationClaims = null;
        // #1195 Phase 2 (Task 6b, A) — the F111 async lifecycle fires for BOTH external-wallet
        // presentation sources. HAIP keeps first pick (byte-identical behaviour for existing
        // blueprints); a SorchaWallet requirement now also initiates instead of falling through
        // to the internal synchronous verifier (which could never satisfy an async wallet gate).
        var presentationRequirement = actionDef.CredentialRequirements?
            .FirstOrDefault(r => r.PresentationSource == PresentationSource.HaipExternalWallet)
            ?? actionDef.CredentialRequirements?
            .FirstOrDefault(r => r.PresentationSource == PresentationSource.SorchaWallet);
        if (actionDef.CredentialRequirements?.Any() == true)
        {
            var hasSubmittedPresentations = request.CredentialPresentations is { Count: > 0 };

            if (presentationRequirement != null && !hasSubmittedPresentations && _presentationLifecycle != null)
            {
                // Feature 111 — timebound presentation lifecycle. The attempt itself is
                // recorded on the register via a PresentationInitiated transaction; the
                // action does NOT complete here. The verifier callback writes the
                // PresentationOutcome which (on success) advances the action.
                //
                // US3 retry gate: if any prior PresentationOutcome with kind=success
                // exists for this instance+action, the action is already complete and
                // a fresh attempt would be meaningless. Return 409 Conflict. Prior
                // decline/abandoned outcomes do NOT block — retry is first-class.
                await AssertNoPriorSuccessfulPresentationAsync(
                    instance, actionId, cancellationToken);

                if (_presentationRateLimiter != null)
                {
                    var rateCheck = await _presentationRateLimiter.CheckAsync(
                        request.SenderWallet, instance.RegisterId, cancellationToken);
                    if (!rateCheck.Allowed)
                    {
                        _logger.LogWarning(
                            "PresentationCallbackRejected rate-limited wallet={Wallet} register={RegisterId} count={Count}/{Threshold}",
                            request.SenderWallet, instance.RegisterId, rateCheck.CurrentCount, rateCheck.Threshold);
                        _presentationMetrics?.RecordRateLimitRejected(request.SenderWallet, instance.RegisterId);
                        throw new PresentationRateLimitedException(rateCheck.RetryAfter);
                    }
                }

                var lifecycleResult = await _presentationLifecycle.InitiateAsync(
                    blueprint, instance, actionDef, presentationRequirement,
                    submitterWallet: request.SenderWallet,
                    delegationToken: delegationToken,
                    draftPayload: request.PayloadData,
                    previousTransactionId: instance.LastTransactionId,
                    cancellationToken);

                _logger.LogInformation(
                    "Presentation lifecycle initiated: requestId={RequestId} attemptTx={TxId} expiresAt={ExpiresAt}",
                    lifecycleResult.PresentationRequestId, lifecycleResult.InitiatedTransactionId, lifecycleResult.ExpiresAt);

                return new ActionSubmissionResponse
                {
                    TransactionId = lifecycleResult.InitiatedTransactionId,
                    InstanceId = instance.Id,
                    IsComplete = false,
                    AwaitingPresentation = true,
                    PresentationRequest = new PresentationRequestResponse
                    {
                        RequestId = lifecycleResult.PresentationRequestId,
                        PresentationRequestUri = lifecycleResult.AuthorizationRequestUri,
                        CredentialType = presentationRequirement.Type,
                        RequestedClaims = presentationRequirement.RequiredClaims?
                            .Select(c => c.ClaimName).ToList(),
                        ExpiresAt = lifecycleResult.ExpiresAt,
                        // Both of these used to be dropped here. Source is what tells the client
                        // which lifecycle to poll; without it every gate went to HAIP.
                        Source = presentationRequirement.PresentationSource,
                        ClaimsFetchToken = lifecycleResult.ClaimsFetchToken
                    }
                };
            }
            else if (presentationRequirement != null && !hasSubmittedPresentations)
            {
                // Deployment-configuration error: this branch is only reachable when
                // IPresentationLifecycleService is not registered. Fail fast rather
                // than silently skipping the presentation requirement.
                throw new InvalidOperationException(
                    $"An external-wallet presentation ({presentationRequirement.PresentationSource}) was requested " +
                    "but IPresentationLifecycleService is not registered. Ensure PresentationLifecycleOptions " +
                    "and related services are wired in the DI container.");
            }
            else if (_credentialVerifier != null)
            {
                // Internal Sorcha credential verification (existing path)
                var presentations = request.CredentialPresentations ?? [];
                var credentialResult = await _credentialVerifier.VerifyAsync(
                    actionDef.CredentialRequirements,
                    presentations,
                    cancellationToken);

                if (!credentialResult.IsValid)
                {
                    var credentialErrors = credentialResult.Errors
                        .Select(e => $"Credential: {e.Message}")
                        .ToList();
                    throw new ValidationException(credentialErrors);
                }

                _logger.LogInformation(
                    "Credential verification passed for action {ActionId}: {Count} credential(s) verified",
                    actionId, credentialResult.VerifiedCredentials.Count);

                // Feature 174 / #1195 Phase 2 — capture the disclosed claims from the verified
                // presentation(s) so they can be exposed under /presentedCredential/* at issuance
                // (step 8a-bis). Merge across all verified credentials (a single root credential in
                // the AIAS device-binding flow). Previously this result was dropped after logging,
                // which meant a claim mapped from /presentedCredential/* fell through to whatever the
                // client submitted — the vulnerability this closes.
                if (credentialResult.VerifiedCredentials.Count > 0)
                {
                    var captured = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var verified in credentialResult.VerifiedCredentials)
                    {
                        foreach (var claim in verified.VerifiedClaims)
                        {
                            captured[claim.Key] = claim.Value;
                        }
                    }
                    verifiedPresentationClaims = captured;
                }
            }
        }

        // 5. Reconstruct accumulated state from prior transactions
        var accumulatedState = await _stateReconstruction.ReconstructAsync(
            blueprint,
            instanceId,
            actionId,
            instance.RegisterId,
            delegationToken,
            instance.ParticipantWallets,
            cancellationToken);

        activity?.SetTag("state.action_count", accumulatedState.ActionCount);

        // 5b. Fall back to instance-tracked LastTransactionId when register query fails.
        // The Register Service may not support instance-based transaction queries yet,
        // but the instance tracks the last confirmed transaction from prior executions.
        if (string.IsNullOrEmpty(accumulatedState.PreviousTransactionId) && !string.IsNullOrEmpty(instance.LastTransactionId))
        {
            _logger.LogInformation(
                "Using instance-tracked LastTransactionId {TxId} as previous transaction for action {ActionId}",
                instance.LastTransactionId, actionId);
            accumulatedState = accumulatedState with { PreviousTransactionId = instance.LastTransactionId };
        }

        // 5c. A starting action with no prior transaction chains from the transaction that PUBLISHED
        // the definition this instance runs (Feature 195). Each instance forks from it by design —
        // the validator permits multiple children of a Control-typed predecessor.
        //
        // READ from the instance's pin, never recomputed. Anchor and pin became one value because
        // they are one fact: the derivation this replaced had FOUR homes and was version-blind, so
        // every republish deduped into a single, silently-dropped transaction (#1563).
        //
        // The confirmation wait is retained deliberately. It is a genuine PRECONDITION — this
        // definition is really sealed on this register — and under pinning it asserts something
        // stronger than before: the exact definition, not merely the blueprint.
        if (string.IsNullOrEmpty(accumulatedState.PreviousTransactionId) && actionDef.IsStartingAction)
        {
            var definitionTxId = instance.BlueprintDefinitionTxId;
            if (string.IsNullOrWhiteSpace(definitionTxId))
            {
                throw new InvalidOperationException(
                    $"Instance {instanceId} carries no definition pin, so its starting action has " +
                    "nothing to chain from. Refusing rather than guessing a definition.");
            }

            await WaitForTransactionConfirmationAsync(instance.RegisterId, definitionTxId, cancellationToken);

            _logger.LogInformation(
                "Action 0 for instance {InstanceId}: PrevTxId set to its definition's publication {DefinitionTxId}",
                instanceId, definitionTxId);

            accumulatedState = accumulatedState with { PreviousTransactionId = definitionTxId };
        }

        // 5d. Starting action participant binding — bind sender wallet to participant role.
        //
        // Feature 103 US1: this is the canonical late-binding block for open
        // starting actions. The contract is:
        //   - IsStartingAction = true on an action means the participant named by
        //     actionDef.Sender is OPEN and may be late-bound by the first submitter.
        //   - The participant's WalletAddress in the blueprint MUST be null at publish
        //     time (enforced by the VAL_BP_010 publish-time guardrail). When the
        //     blueprint instead pre-binds the participant, the strict-equality check
        //     at lines 196-216 above would reject every real public submitter before
        //     this block even runs.
        //   - Once bound, the binding is immutable for the life of the instance.
        //
        // Persistence: the binding is written through to BOTH the authoritative
        // IInstanceStore (via UpdateAsync below — this is the source of truth, and
        // the Validator service's chain conformance check rebuilds it from the
        // signed Action transaction on the ledger if ever needed) AND the Redis
        // read-through cache (via IInstanceBindingCache.SetAsync — performance layer
        // only; failure is non-fatal). See T014a investigation:
        // specs/103-verified-citizen-v2/investigation-t014a.md — the Program.cs:883
        // legacy endpoint bypasses this path entirely but is not used by walkthroughs
        // or the UI.
        //
        // Design note (why the read-path here does NOT consult IInstanceBindingCache):
        // the `instance` variable has already been hydrated from IInstanceStore at
        // line 138. Reading `instance.ParticipantWallets` is a local in-memory
        // dictionary lookup; a cache round-trip would be strictly slower. The cache
        // exists for OTHER call sites that want to resolve a binding without loading
        // the full Instance (e.g. a disclosure resolver or a SignalR notification
        // dispatcher that only needs the participant→wallet map). Those consumers
        // land in follow-up waves — this site writes through so the cache is warm
        // for them.
        if (actionDef.IsStartingAction && !string.IsNullOrWhiteSpace(actionDef.Sender))
        {
            var senderParticipantId = actionDef.Sender;
            if (instance.ParticipantWallets.TryGetValue(senderParticipantId, out var boundWallet))
            {
                // Already bound — verify it's the same wallet (FR-004: immutable binding)
                if (!string.Equals(boundWallet, request.SenderWallet, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Participant '{senderParticipantId}' is already bound to wallet {boundWallet}. " +
                        $"Cannot rebind to {request.SenderWallet} (instance bindings are immutable).");
                }
            }
            else
            {
                // Bind sender wallet to participant role. Persist authoritatively to the
                // instance store first, then write through to the cache.
                instance.ParticipantWallets[senderParticipantId] = request.SenderWallet;
                await _instanceStore.UpdateAsync(instance, cancellationToken);

                // Write-through to the Redis cache — best-effort, never fails the caller.
                if (_bindingCache is not null)
                {
                    await _bindingCache.SetAsync(
                        instanceId,
                        instance.ParticipantWallets,
                        cancellationToken);
                }

                _logger.LogInformation(
                    "Bound wallet {Wallet} to participant '{ParticipantId}' for instance {InstanceId}",
                    request.SenderWallet, senderParticipantId, instanceId);
            }
        }

        // 6a. Merge any prepopulated payload seeded by a previous action's
        //     Route.OutputMapping into the submitted payload BEFORE validation.
        //     Submitted values take precedence on key collision (FR-007).
        //     Feature 104 wave 14a.
        var seedActionId = actionDef.Id;
        if (instance.PendingActionPayloads.TryGetValue(seedActionId, out var seedPayload)
            && seedPayload is not null
            && seedPayload.Count > 0)
        {
            var mergedPayloadData = new Dictionary<string, object>(request.PayloadData.Count + seedPayload.Count);

            // Start with seeded fields
            foreach (var kvp in seedPayload)
            {
                if (kvp.Value is null)
                {
                    continue;
                }
                // Deep-clone the node so mutations in later engine stages don't
                // bleed back into the stored seed.
                var cloned = JsonNode.Parse(kvp.Value.ToJsonString());
                if (cloned is not null)
                {
                    mergedPayloadData[kvp.Key] = cloned;
                }
            }

            // Overlay submitted fields (submission wins on collision)
            foreach (var kvp in request.PayloadData)
            {
                mergedPayloadData[kvp.Key] = kvp.Value;
            }

            request = request with { PayloadData = mergedPayloadData };
        }

        // 6a-bis. Issue #1264 — resolve every x-claim-source binding SERVER-SIDE, from live state,
        //     and overwrite whatever the client sent.
        //
        //     Feature 183 US1 originally seeded these bindings client-side from the browser's JWT, so
        //     the value was only ever as fresh as the token the client happened to hold. A citizen's
        //     token was minted at signup carrying email_verified:false; they verified nine minutes
        //     later; the application they submitted five minutes after that was auto-rejected on the
        //     stale false. Verifying updates server state but cannot rewrite an issued token, and
        //     nothing re-mints it — so this affects any user who verifies mid-session, which is the
        //     normal signup order.
        //
        //     Resolving here does two things at once. It kills the staleness class rather than one
        //     instance of it (the value is read at the moment it is used), and because the server
        //     overwrites the submitted value, a client can no longer assert a field the platform is
        //     supposed to vouch for. Both matter: these fields gate identity decisions.
        //
        //     Placed BEFORE validation (6b) deliberately, so the value the server vouches for is the
        //     one that gets validated, signed, sealed and disclosed — request.PayloadData is what the
        //     signed transaction is built from (see payloadWithCalculations below).
        var claimSourceBindings = ClaimSourceBindings.Discover(actionDef.DataSchemas);
        if (claimSourceBindings.Count > 0)
        {
            var bindings = claimSourceBindings;

            // The submission is signed by a server-custodied wallet on the caller's behalf, so the
            // caller principal is who the claims are about.
            var platformUserIdClaim = caller?.FindFirst(TokenClaimConstants.PlatformUserId)?.Value;
            if (!Guid.TryParse(platformUserIdClaim, out var platformUserId))
            {
                // Fail loudly rather than fall back to the token's own copy of the claim: that
                // fallback IS the #1264 defect. An action whose schema declares a claim-source
                // binding is asserting that the platform vouches for the value, which is only
                // meaningful for a caller the platform can identify.
                throw new InvalidOperationException(
                    $"Action {actionId} declares {bindings.Count} x-claim-source binding(s) but the caller "
                    + $"carries no usable '{TokenClaimConstants.PlatformUserId}' claim, so their live "
                    + "values cannot be resolved. Refusing to submit rather than stamp an unverified value.");
            }

            if (_platformUserClaims is null)
            {
                throw new InvalidOperationException(
                    $"Action {actionId} declares x-claim-source binding(s) but no "
                    + $"{nameof(IPlatformUserClaimsClient)} is registered, so live values cannot be "
                    + "resolved. Refusing to submit rather than stamp an unverified value.");
            }

            IReadOnlyDictionary<string, string> live;
            try
            {
                live = await _platformUserClaims.ResolveAsync(
                    platformUserId, ClaimSourceBindings.ClaimNames(bindings), cancellationToken);
            }
            catch (PlatformUserClaimsUnavailableException ex)
            {
                // Fail the submission. Signing a defaulted false would write an irreversible wrongful
                // rejection onto the ledger for a transient reason; a failed submission is recoverable
                // because the citizen simply retries.
                _logger.LogError(ex,
                    "Could not resolve live claim values for platform user {PlatformUserId} on action "
                    + "{ActionId} of instance {InstanceId}; refusing the submission",
                    platformUserId, actionId, instanceId);
                throw new InvalidOperationException(
                    "Could not confirm your account details with the platform, so this submission was not "
                    + "sent. Nothing has been recorded — please try again.", ex);
            }

            var payload = new Dictionary<string, object>(request.PayloadData);
            foreach (var binding in bindings)
            {
                live.TryGetValue(binding.ClaimName, out var claimValue);
                var coerced = ClaimSourceBindings.Coerce(binding, claimValue);

                if (coerced is null)
                {
                    // Unresolved non-boolean binding: remove, so a client-asserted or stale value can
                    // never stand in for one the server declined to vouch for.
                    payload.Remove(binding.PropertyName);
                }
                else
                {
                    payload[binding.PropertyName] = coerced;
                }

                if (!live.ContainsKey(binding.ClaimName))
                {
                    _logger.LogWarning(
                        "Action {ActionId} binds property '{Property}' to claim '{Claim}', which the "
                        + "platform does not resolve; the binding failed closed",
                        actionId, binding.PropertyName, binding.ClaimName);
                }
            }

            request = request with { PayloadData = payload };

            _logger.LogInformation(
                "Resolved {Count} x-claim-source binding(s) server-side for action {ActionId} of "
                + "instance {InstanceId} (platform user {PlatformUserId})",
                bindings.Count, actionId, instanceId, platformUserId);
        }

        // 6b. Validate input data against schema
        var validationResult = await ValidateActionDataAsync(actionDef, request.PayloadData, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        // 7. Merge accumulated state with current data for routing and calculations.
        //    Prefer register-reconstructed state; fall back to instance-stored accumulated data
        //    when the Register Service doesn't support instance-based transaction queries.
        var flattenedState = accumulatedState.GetFlattenedData();
        var mergedData = flattenedState.Count > 0
            ? flattenedState.Where(kvp => kvp.Value != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value!)
            : new Dictionary<string, object>(instance.AccumulatedData);

        // Feature 174 / #1195 Phase 2 — snapshot any /presentedCredential source carried forward from a
        // prior gated action's reconstructed data BEFORE the payload overlay, so a client-supplied
        // `presentedCredential` field can never masquerade as the verified source. Reinstated with
        // precedence at step 8a-bis. See PresentedCredentialClaimSourceKey.
        var reconstructedPresentedCredential =
            mergedData.TryGetValue(PresentedCredentialClaimSourceKey, out var reconstructedPc)
                ? reconstructedPc
                : null;

        foreach (var kvp in request.PayloadData)
        {
            mergedData[kvp.Key] = kvp.Value;
        }

        // 8. Apply calculations BEFORE routing so calculated values (e.g. riskScore)
        //    are available for route condition evaluation in this and subsequent actions
        var calculations = await EvaluateCalculationsAsync(actionDef, mergedData, cancellationToken);
        if (calculations != null)
        {
            foreach (var kvp in calculations)
            {
                mergedData[kvp.Key] = kvp.Value;
            }
        }

        // 8a. Feature 155 (T026) — inject an issuanceContext so credential claim
        //     mappings can anchor to the register the credential is issued on.
        //     A claim mapping with sourceField "/issuanceContext/registerId" resolves
        //     to this value; the open verifier reads the resulting `registerAnchor`
        //     claim (plus the credential's own jti) to call the public anchor endpoint.
        //     Injected before any BuildClaimsFromMappings call (HAIP at step 8b and
        //     SorchaLocalWallet at step 8c) so the pointer resolves on every issuance path.
        mergedData["issuanceContext"] = new Dictionary<string, object?>
        {
            ["registerId"] = instance.RegisterId
        };

        // 8a-bis. Feature 174 / #1195 Phase 2 — expose the verified presentation's disclosed claims
        //     under the stable /presentedCredential/* claim-source prefix (see
        //     PresentedCredentialClaimSourceKey) and give them PRECEDENCE over client payload. A
        //     claimMappings entry with sourceField "/presentedCredential/givenName" resolves the
        //     givenName disclosed by the verified, issuer-signed root presentation — the device-copy's
        //     identity claims are carried forward from that verified presentation (design §4.1), never
        //     from client-supplied payload. Two supply routes: (a) this execution's synchronous gate
        //     (verifiedPresentationClaims), (b) a prior gated action's reconstructed data
        //     (reconstructedPresentedCredential, snapshotted pre-overlay at step 7). Whichever is
        //     present is written here AFTER the payload overlay, so any payload-supplied
        //     `presentedCredential` field is dropped. When neither is present the key is removed
        //     outright — a client value must never be able to pose as a verified presentation
        //     (fail-closed; /presentedCredential/* then resolves to nothing and the claim is dropped).
        var trustedPresentedCredential = (object?)verifiedPresentationClaims ?? reconstructedPresentedCredential;
        if (trustedPresentedCredential is not null)
        {
            mergedData[PresentedCredentialClaimSourceKey] = trustedPresentedCredential;
        }
        else
        {
            mergedData.Remove(PresentedCredentialClaimSourceKey);
        }

        // 8b. HAIP credential mint (Feature 097 + Feature 104 wave 14b).
        //     Moved to run BEFORE routing so the minted offer data can be
        //     carried forward to the claim action via Route.OutputMapping.
        //     Internal Sorcha issuance (issuedCredential) still runs later at
        //     step 9d because it depends on the built transaction context.
        // Caller's org context — used both for the HAIP offer (so HAIP can swap to the
        // org's issuance key) and for SorchaLocalWallet credential issuance below.
        var callerIssuerOrgName = caller?.FindFirst("org_name")?.Value;
        var callerIssuerTenantId = caller?.FindFirst("org_id")?.Value;

        // Client-facing warnings raised while building the credential claims (e.g. the F107 portrait
        // size gate dropping an oversized image). Surfaced on the response so the drop is visible to the
        // submitter, not only in the server log (issue #340). Threaded to both the HAIP and internal paths.
        var credentialWarnings = new List<string>();

        // Feature 176 / FR-004 / SC-003: an action can carry a credentialIssuanceConfig yet gate the
        // actual mint on the submitted decision via an optional issuanceCondition. When it evaluates
        // falsy (e.g. an agent's decision=="rejected"), NO credential is minted or delivered — the
        // action still routes onward per its routes. Null condition → always issue (pre-existing
        // behaviour); an unevaluable condition fails closed (no issuance).
        var credentialIssuanceAllowed = EvaluateIssuanceCondition(actionDef, mergedData!);

        CreateOfferResult? haipOfferResult = null;
        if (actionDef.CredentialIssuanceConfig != null
            && credentialIssuanceAllowed
            && actionDef.CredentialIssuanceConfig.TargetAudience == TargetAudience.HaipExternalWallet)
        {
            if (_haipClient is null)
            {
                // HAIP-targeted issuance without a registered HAIP client is a
                // misconfiguration: the action expects an external-wallet offer
                // but no service is available to mint one. Surface this in
                // observability so deployment-time gaps don't silently produce
                // empty claim actions downstream.
                _logger.LogError(
                    "Action {ActionId} on blueprint {BlueprintId} declares TargetAudience=HaipExternalWallet " +
                    "but no IHaipClient is registered. HAIP offer will not be minted; downstream claim " +
                    "action (if any) will have an empty credentialOffer seed and will fall back to the " +
                    "default form renderer. Check service registration.",
                    actionDef.Id, blueprint.Id);
            }
            else
            {
                var haipClaims = BuildClaimsFromMappings(
                    actionDef.CredentialIssuanceConfig.ClaimMappings,
                    mergedData!,
                    credentialWarnings);
                var haipClaimsForWire = haipClaims.ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

                _logger.LogInformation(
                    "Routing credential issuance to HAIP service for external wallet: type={Type}, claims=[{ClaimNames}]",
                    actionDef.CredentialIssuanceConfig.CredentialType,
                    string.Join(", ", haipClaimsForWire.Keys));

                // Feature 120 — pass the issuer's actual TenantId (org_id from caller
                // JWT) so HAIP's /credential endpoint can swap to the org's issuance
                // key. Previously this argument was `instance.RegisterId`, which made
                // the offer's TenantId a register UUID and bypassed the kid-swap path.
                haipOfferResult = await _haipClient.CreateCredentialOfferAsync(
                    request.SenderWallet,
                    callerIssuerTenantId ?? instance.RegisterId,
                    actionDef.CredentialIssuanceConfig.CredentialType,
                    haipClaimsForWire,
                    actionDef.CredentialIssuanceConfig.Disclosable?.ToList(),
                    cancellationToken);

                _logger.LogInformation(
                    "HAIP credential offer created: offerId={OfferId}, expiresAt={ExpiresAt}",
                    haipOfferResult.OfferId, haipOfferResult.ExpiresAt);
            }
        }

        // 8c. Feature 106 — mint a register-native credential for on-platform wallets
        //     (SorchaLocalWallet or deprecated SorchaInternal) BEFORE routing and disclosure.
        //     The freshly minted credential is sealed into the recipient-addressed disclosure
        //     group at step 9b so it rides the existing encryption pipeline and peer-replicates
        //     to the holder's Wallet Service (which extracts it via Wave B inbound credential
        //     detection). This is the ONLY delivery path for on-platform credentials — direct
        //     wallet writes are never used because they break on multi-node deployments.
        //
        //     Runtime error codes:
        //       VAL_RUNTIME_CRED_001 — recipient wallet not resolvable (late-binding not yet run)
        //       VAL_RUNTIME_CRED_002 — credential mint failed
        //       VAL_RUNTIME_CRED_003 — encryption of the sealed credential failed (raised at step 9d)
        //
        //     Contract: specs/106-register-native-credentials/contracts/credential-issuance-config.md
        CredentialIssuanceResult? localWalletCredential = null;
        string? localWalletRecipient = null;
        // Feature 137 / C3 — carried encryption key injected into the encryption pipeline at
        // step 9d when the recipient is an open-participant citizen with no published participant
        // record (cross-node late binding). Null on the pre-137 (published/derivation) path.
        ExternalKeyInfo? crossNodeDeliveryKey = null;
        // Reuse the caller's org context computed earlier (used by both the HAIP path
        // and SorchaLocalWallet path).
        var issuerOrgName = callerIssuerOrgName;
        var issuerTenantId = callerIssuerTenantId;
        // Accept the deprecated SorchaInternal alongside SorchaLocalWallet so pre-migration
        // blueprints still resolve to the local-wallet delivery path.
#pragma warning disable CS0618
        if (actionDef.CredentialIssuanceConfig != null
            && credentialIssuanceAllowed
            && actionDef.CredentialIssuanceConfig.TargetAudience is TargetAudience.SorchaLocalWallet
                or TargetAudience.SorchaInternal)
#pragma warning restore CS0618
        {
            var recipientId = actionDef.CredentialIssuanceConfig.RecipientParticipantId;
            if (string.IsNullOrWhiteSpace(recipientId)
                || !instance.ParticipantWallets.TryGetValue(recipientId, out localWalletRecipient)
                || string.IsNullOrEmpty(localWalletRecipient))
            {
                throw new InvalidOperationException(
                    $"[VAL_RUNTIME_CRED_001] SorchaLocalWallet issuance for action {actionDef.Id} requires " +
                    $"recipient participant '{recipientId}' to be bound to a wallet on the instance. " +
                    $"Ensure the recipient has submitted a prior action or is pre-bound in the published blueprint.");
            }

            // Feature 137 / C3 — when the blueprint opts in via HolderKeySourceField, resolve the
            // recipient's delivery keys with FR-012 precedence BEFORE minting so a credential that
            // cannot be bound + delivered is never issued (SC-004 fail-closed). Blueprints that leave
            // HolderKeySourceField null keep the pre-137 behaviour (no cnf binding; recipient key
            // resolved from the register or derived from the Ed25519 signing key at step 9d).
            JsonElement? holderJwk = null;
            var holderKeySourceField = actionDef.CredentialIssuanceConfig.HolderKeySourceField;
            if (!string.IsNullOrWhiteSpace(holderKeySourceField))
            {
                var carried = ResolveCarriedHolderKeys(mergedData, holderKeySourceField);
                holderJwk = carried.HolderJwk;

                // Delivery (encryption) key precedence: (1) published participant record wins;
                // (2) carried key fallback; (3) fail closed. ResolvePublicKeyAsync returns null on
                // not-found and throws on revoked (410) — a revoked recipient is a hard stop, which
                // is the correct fail-closed outcome (no delivery to a revoked participant).
                var publishedKey = await _registerClient.ResolvePublicKeyAsync(
                    instance.RegisterId, localWalletRecipient, cancellationToken: cancellationToken);
                if (publishedKey is not null)
                {
                    _logger.LogInformation(
                        "[137] Recipient {Recipient} resolved from a published participant record on register {RegisterId} — published delivery key wins.",
                        localWalletRecipient, instance.RegisterId);
                }
                else if (!string.IsNullOrEmpty(carried.EncryptionPublicKey)
                         && !string.IsNullOrEmpty(carried.Algorithm))
                {
                    crossNodeDeliveryKey = new ExternalKeyInfo
                    {
                        PublicKey = carried.EncryptionPublicKey!,
                        Algorithm = carried.Algorithm!
                    };
                    _logger.LogInformation(
                        "[137] Recipient {Recipient} has no published participant record on register {RegisterId} — using the carried delivery key ({Algorithm}).",
                        localWalletRecipient, instance.RegisterId, carried.Algorithm);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"[VAL_RUNTIME_CRED_004] SorchaLocalWallet issuance for action {actionDef.Id} could not resolve a delivery key for recipient " +
                        $"'{localWalletRecipient}': no published participant record on register {instance.RegisterId} and no carried encryption key in the submission. " +
                        $"Failing closed without issuing a credential (FR-012 / SC-004).");
                }

                // FR-014 — the credential MUST be bound to the recipient's holder key. A configured
                // HolderKeySourceField that resolves no holder JWK is a fail-closed condition.
                if (holderJwk is null)
                {
                    throw new InvalidOperationException(
                        $"[VAL_RUNTIME_CRED_005] SorchaLocalWallet issuance for action {actionDef.Id} is configured with HolderKeySourceField " +
                        $"'{holderKeySourceField}' but no holder JWK resolved from the submission. Failing closed — the credential cannot be " +
                        $"bound to the recipient's holder key (FR-014).");
                }
            }

            try
            {
                localWalletCredential = await IssueCredentialFromActionAsync(
                    actionDef, mergedData, request.SenderWallet, instance, issuerOrgName, issuerTenantId, holderJwk, credentialWarnings, cancellationToken);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"[VAL_RUNTIME_CRED_002] SorchaLocalWallet credential mint failed for action {actionDef.Id}: {ex.Message}",
                    ex);
            }

            if (localWalletCredential is null)
            {
                throw new InvalidOperationException(
                    $"[VAL_RUNTIME_CRED_002] SorchaLocalWallet credential mint returned null for action {actionDef.Id}. " +
                    $"The Wallet Service IssueCredentialAsync path reported no result — check Wallet Service logs.");
            }

            _logger.LogInformation(
                "Minted SorchaLocalWallet credential {CredentialId} (type {Type}) for recipient {Recipient}. " +
                "Will seal into the action's recipient-addressed disclosure group.",
                localWalletCredential.CredentialId, localWalletCredential.Type, localWalletRecipient);
        }

        // 9. Evaluate routing conditions to determine next action(s).
        //    Build the output source document for Route.OutputMapping evaluation
        //    (Feature 104 wave 14a). Payload, calculations, and (when present)
        //    HAIP mint output are exposed under /payload, /calculations, /haip.
        var outputSource = BuildOutputMappingSource(request.PayloadData, calculations, haipOfferResult, actionDef);
        var routingResult = await EvaluateRoutingAsync(blueprint, actionDef, mergedData, outputSource, cancellationToken);

        // Feature 184: the decision notice is NO LONGER fired here. Firing inline would fire it on the
        // node that processed THIS submission — the deciding participant's node — where the recipient
        // (a citizen, typically on another node entirely) has no account and no inbox. Instead the
        // taken route's id and the reason code ride the signed routing decision (step 10d below), and
        // the ReactionDispatcher fires the notice on whichever node hosts the recipient's wallet, as
        // that node folds the sealed transaction.

        // 9a. Build payload that includes calculated values so they persist in the transaction
        //     and are available during state reconstruction for subsequent actions' routing
        var payloadWithCalculations = new Dictionary<string, object>(request.PayloadData);
        if (calculations != null)
        {
            foreach (var kvp in calculations)
            {
                payloadWithCalculations[kvp.Key] = kvp.Value;
            }
        }

        // 9b. Apply disclosure rules for recipients
        var disclosedPayloads = await ApplyDisclosuresAsync(actionDef, payloadWithCalculations, blueprint, instance.ParticipantWallets, instance.RegisterId);

        // If no disclosure rules defined, default to full disclosure under sender's wallet.
        // This ensures the payload data is always present in the transaction for schema validation.
        if (disclosedPayloads.Count == 0 && payloadWithCalculations.Count > 0)
        {
            disclosedPayloads[request.SenderWallet] = payloadWithCalculations;
        }

        // 9b-bis. Feature 106 — seal the SorchaLocalWallet credential into the recipient's
        //         disclosure group so it rides the encryption pipeline and peer-replicates.
        //         If a blueprint-defined disclosure for the recipient already exists, the
        //         credential is merged into it under the /credential key; otherwise a new
        //         recipient entry is created. Wave B's InboundCredentialDetector on the
        //         holder's Wallet Service extracts from this exact shape.
        if (localWalletCredential is not null && !string.IsNullOrEmpty(localWalletRecipient))
        {
            if (!disclosedPayloads.TryGetValue(localWalletRecipient, out var recipientFields))
            {
                recipientFields = new Dictionary<string, object>();
                disclosedPayloads[localWalletRecipient] = recipientFields;
            }

            // claude-review PR#294: do NOT coerce ExpiresAt to empty string when null.
            // The detector's TryGetDateTimeOffset fallback on "" gives the right outcome
            // only by accident; let the serialiser emit null (or omit the key) so the
            // read path stays deterministic.
            var credentialDict = new Dictionary<string, object?>
            {
                ["credentialId"] = localWalletCredential.CredentialId,
                ["credentialType"] = localWalletCredential.Type,
                ["displayConfig"] = localWalletCredential.DisplayConfigJson,
                ["issuerDid"] = localWalletCredential.IssuerDid,
                ["subjectDid"] = localWalletCredential.SubjectDid,
                ["issuedAt"] = localWalletCredential.IssuedAt,
                ["expiresAt"] = localWalletCredential.ExpiresAt,
                ["rawToken"] = localWalletCredential.RawToken,
                ["issuerOrgName"] = issuerOrgName,
                ["issuanceBlueprintId"] = instance.BlueprintId,
                ["issuanceInstanceId"] = instanceId,
                ["issuanceActionId"] = actionId.ToString(),
                ["claimActionId"] = routingResult.NextActions.FirstOrDefault()?.ActionId.ToString(),
                ["registerId"] = instance.RegisterId,
            };
            recipientFields["/credential"] = credentialDict;
        }

        // 9b-ter. Feature 145: generate the human-readable instance reference here, PRE-SUBMIT, for
        //         BOTH the DevMode and encrypted paths. This is the single point with the plaintext
        //         first-action data regardless of register encryption, and persisting it before the
        //         transaction can seal avoids racing the InstanceProjector — which is the sole writer
        //         of control state (CurrentActionIds / CompletedActionCount / State) and never touches
        //         this metadata key. Idempotent on the key, so only the first action that lacks it
        //         pays the extra write. (Pre-145 this lived in the post-confirmation advance on both
        //         the inline and encrypted paths; those advances are gone.)
        if (!instance.Metadata.ContainsKey("instanceReference"))
        {
            var instanceRef = Sorcha.Blueprint.Engine.Implementation.InstanceReferenceGenerator.Generate(
                blueprint.InstanceReference,
                mergedData,
                instance.Id,
                blueprint.Title);
            instance.Metadata["instanceReference"] = instanceRef;
            instance = await _instanceStore.UpdateAsync(instance, cancellationToken);
            _logger.LogInformation(
                "Generated instance reference {Reference} for instance {InstanceId} (pre-submit, Feature 145)",
                instanceRef, instance.Id);
        }

        // 9c. Check register DevMode — skip encryption for DevMode registers
        var registerDevMode = false;
        try
        {
            var register = await _registerClient.GetRegisterAsync(instance.RegisterId, cancellationToken);
            registerDevMode = register?.DevMode ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check register DevMode for {RegisterId}, defaulting to encrypted path",
                instance.RegisterId);
        }

        // 9d. Encrypt disclosed payloads (envelope encryption) — skipped for DevMode registers
        EncryptionResult? encryptionResult = null;
        DisclosureGroup[]? disclosureGroups = null;
        if (!registerDevMode && _encryptionPipeline != null && disclosedPayloads.Count > 0)
        {
            // US4: Automatic register resolution with external key override.
            // Feature 137 / C3 — merge any carried cross-node delivery key for the SorchaLocalWallet
            // recipient into the external-key set so the pipeline can wrap the credential disclosure
            // to an open participant who has no published participant record. Honours "published wins"
            // because crossNodeDeliveryKey is only populated (at step 8c) when the register lookup
            // missed, and TryAdd never overrides an explicitly-supplied external key.
            var effectiveExternalKeys = request.ExternalRecipientKeys;
            if (crossNodeDeliveryKey is not null && !string.IsNullOrEmpty(localWalletRecipient))
            {
                effectiveExternalKeys = effectiveExternalKeys is null
                    ? new Dictionary<string, ExternalKeyInfo>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, ExternalKeyInfo>(effectiveExternalKeys, StringComparer.OrdinalIgnoreCase);
                effectiveExternalKeys.TryAdd(localWalletRecipient, crossNodeDeliveryKey);
            }

            var (recipients, resolveError) = await ResolveRecipientKeysAsync(
                disclosedPayloads.Keys, effectiveExternalKeys, instance.RegisterId, cancellationToken);
            if (resolveError != null)
            {
                throw new InvalidOperationException(resolveError);
            }

            // T008: Populate DisplayName on RecipientInfo for UI progress events
            // Build wallet→name reverse lookup from blueprint participants + instance bindings
            var walletToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (blueprint.Participants != null)
            {
                foreach (var p in blueprint.Participants)
                {
                    if (instance.ParticipantWallets.TryGetValue(p.Id, out var boundWallet))
                    {
                        walletToName.TryAdd(boundWallet, p.Name ?? p.Id);
                    }
                    if (!string.IsNullOrEmpty(p.WalletAddress))
                    {
                        walletToName.TryAdd(p.WalletAddress, p.Name ?? p.Id);
                    }
                }
            }

            foreach (var r in recipients)
            {
                if (walletToName.TryGetValue(r.WalletAddress, out var name))
                {
                    r.DisplayName = name;
                }
                else
                {
                    // Fallback: truncated wallet address (first 8...last 4)
                    r.DisplayName = r.WalletAddress.Length > 12
                        ? $"{r.WalletAddress[..8]}...{r.WalletAddress[^4..]}"
                        : r.WalletAddress;
                }
            }

            if (recipients.Length > 0)
            {
                // US2: DisclosureGroupBuilder groups recipients with identical field sets
                if (_disclosureGroupBuilder != null)
                {
                    disclosureGroups = _disclosureGroupBuilder.BuildGroups(disclosedPayloads, recipients);
                }
                else
                {
                    // Fallback: build simple disclosure groups (one per wallet)
                    disclosureGroups = disclosedPayloads.Select(kvp =>
                    {
                        var recipient = recipients.FirstOrDefault(r => r.WalletAddress == kvp.Key);
                        if (recipient == null) return null;

                        var fields = kvp.Value.Keys.OrderBy(k => k).ToArray();
                        var groupId = ComputeGroupId(fields, kvp.Value);

                        return new DisclosureGroup
                        {
                            GroupId = groupId,
                            DisclosedFields = fields,
                            FilteredPayload = kvp.Value,
                            Recipients = [recipient]
                        };
                    }).Where(g => g != null).ToArray()!;
                }

                // Async path: offload encryption to background service when channel is available
                if (_encryptionChannel != null && _encryptionOperationStore != null && disclosureGroups!.Length > 0)
                {
                    var operation = await _encryptionOperationStore.CreateAsync(new EncryptionOperation
                    {
                        OperationId = Guid.NewGuid().ToString("N"),
                        BlueprintId = instance.BlueprintId,
                        ActionId = actionId.ToString(),
                        InstanceId = instanceId,
                        SubmittingWalletAddress = request.SenderWallet,
                        TotalRecipients = recipients.Length,
                        TotalGroups = disclosureGroups.Length,
                        TotalSteps = 4
                    });

                    // Build the set of fields allowed in AccumulatedData (payload keys + calculation keys)
                    var asyncAllowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var key in request.PayloadData.Keys)
                        asyncAllowedFields.Add(key);
                    if (actionDef.Calculations != null)
                    {
                        foreach (var calc in actionDef.Calculations)
                            asyncAllowedFields.Add(calc.Key);
                    }

                    var workItem = new EncryptionWorkItem
                    {
                        OperationId = operation.OperationId,
                        InstanceId = instanceId,
                        BlueprintId = instance.BlueprintId,
                        ActionId = actionId,
                        SenderWallet = request.SenderWallet,
                        RegisterId = instance.RegisterId,
                        DisclosureGroups = disclosureGroups!,
                        PayloadWithCalculations = payloadWithCalculations,
                        DisclosedPayloads = disclosedPayloads,
                        PreviousTransactionId = accumulatedState.PreviousTransactionId,
                        UserId = caller?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                            ?? caller?.FindFirst("sub")?.Value,
                        DelegationToken = delegationToken,
                        RoutingResult = routingResult,
                        MergedData = mergedData,
                        AllowedAccumulatedFields = asyncAllowedFields
                    };

                    // Store idempotency key BEFORE writing to channel to prevent duplicate submissions
                    // if the client retries before the background service completes.
                    await _actionStore.StoreIdempotencyKeyAsync(idempotencyKey, operation.OperationId, TimeSpan.FromHours(24));

                    await _encryptionChannel.Writer.WriteAsync(workItem, cancellationToken);

                    _logger.LogInformation(
                        "Encryption offloaded to background service. OperationId: {OperationId}",
                        operation.OperationId);

                    // Return HTTP 202 with operationId for async tracking
                    return new ActionSubmissionResponse
                    {
                        TransactionId = string.Empty, // Will be filled by background service
                        InstanceId = instanceId,
                        OperationId = operation.OperationId,
                        IsAsync = true,
                        NextActions = [],
                        IsComplete = false
                    };
                }

                // Synchronous fallback (channel not injected, e.g. in tests)
                encryptionResult = await _encryptionPipeline.EncryptDisclosedPayloadsAsync(disclosureGroups!, cancellationToken);

                if (!encryptionResult.Success)
                {
                    // Feature 106: when the SorchaLocalWallet credential was sealed into the
                    // disclosed payloads, an encryption failure here means the holder will not
                    // receive the credential via peer sync. Surface with VAL_RUNTIME_CRED_003 so
                    // operators can tell this apart from generic encryption errors.
                    var errorCode = localWalletCredential != null ? "[VAL_RUNTIME_CRED_003] " : string.Empty;
                    throw new InvalidOperationException(
                        $"{errorCode}Encryption failed for recipient {encryptionResult.FailedRecipient}: {encryptionResult.Error}");
                }

                if (encryptionResult.SkippedRecipients.Count > 0)
                {
                    _logger.LogWarning(
                        "Skipped {Count} recipients during encryption (key not resolved): {Recipients}",
                        encryptionResult.SkippedRecipients.Count,
                        string.Join(", ", encryptionResult.SkippedRecipients));
                }
            }
        }

        // 9d. Issue internal Sorcha credential if configured (non-HAIP path).
        //     The HAIP path has already run at step 8b (moved before routing in
        //     Feature 104 wave 14b so Route.OutputMapping can carry offer data
        //     forward to a claim action). Step 8c now handles both SorchaLocalWallet
        //     AND the deprecated SorchaInternal — all on-platform credentials go
        //     through the register disclosure path for multi-node correctness.
        CredentialIssuanceResult? issuedCredential = localWalletCredential;

        // 10. Build transaction
        BuiltTransaction transaction;
        if (encryptionResult?.Success == true && encryptionResult.Groups.Length > 0)
        {
            // Build with encrypted payloads (no plaintext on ledger)
            transaction = await _transactionBuilder.BuildEncryptedActionTransactionAsync(
                blueprint, instance, actionDef,
                payloadWithCalculations,
                encryptionResult.Groups,
                accumulatedState.PreviousTransactionId,
                cancellationToken);
        }
        else
        {
            // Legacy plaintext path (no encryption pipeline or empty payload)
            transaction = await _transactionBuilder.BuildActionTransactionAsync(
                blueprint, instance, actionDef,
                payloadWithCalculations,
                disclosedPayloads,
                accumulatedState.PreviousTransactionId,
                cancellationToken);
        }

        // 10b. Add credential issuance metadata to transaction (T061)
        if (issuedCredential != null)
        {
            transaction.Metadata["credentialId"] = issuedCredential.CredentialId;
            transaction.Metadata["credentialType"] = issuedCredential.Type;
            transaction.Metadata["credentialIssuer"] = issuedCredential.IssuerDid;
            transaction.Metadata["credentialRecipient"] = issuedCredential.SubjectDid;
        }

        // 10d. Feature 145: assemble + sender-sign the full RoutingDecision and carry it on the
        //      transaction's clear metadata. Every node folds decision.nextActions into the
        //      instance projection without decrypting the payload (FR-007/FR-010); the validator
        //      validates it at seal (VAL_ROUTING_*, T023). The full set preserves parallel
        //      branches that the singular nextActionId above collapses. TrackingData copies all
        //      string metadata to the sealed tx, so "routingDecision" rides through to the docket.
        //      Feature 184: the decision also carries the taken route's id and — when that route
        //      declares an x-decision-notice — a non-sensitive reason code resolved from the payload.
        //      Both fall inside ComputeSignableBytes(), so the sender signs them and the validator
        //      verifies them (VAL_ROUTING_002); the recipient's node then renders the notice from the
        //      replicated blueprint without ever reading the payload.
        var routingDecision = new RoutingDecision
        {
            CompletedActionId = actionId,
            NextActions = routingResult.NextActions
                .Select(n => new ActionRef { ActionId = n.ActionId, BranchKey = n.BranchId })
                .ToList(),
            RouteId = routingResult.MatchedRouteId,
            ReasonCode = ResolveDecisionReasonCode(actionDef, routingResult.MatchedRouteId, mergedData),
            // Feature 194: the definition this action was executed against. Read from the instance,
            // never re-derived as "latest" — that is what makes an in-flight instance survive a
            // republish. Inside ComputeSignableBytes below, so the sender signs it.
            BlueprintDefinitionTxId = ResolveInstancePin(instance, actionId),
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned },
        };
        var routingSignResult = await _walletClient.SignTransactionAsync(
            request.SenderWallet,
            routingDecision.ComputeSignableBytes(),
            derivationPath: null,
            isPreHashed: false,
            cancellationToken);
        routingDecision.Attestation.Signature = Convert.ToBase64String(routingSignResult.Signature);
        transaction.Metadata["routingDecision"] =
            JsonSerializer.Serialize(routingDecision, RegisterSerializationOptions.Canonical);

        // 11. Sign transaction using "{TxId}:{PayloadHash}" contract (matches Validator verification)
        var signResult = await _walletClient.SignTransactionAsync(
            request.SenderWallet,
            transaction.SigningData,
            derivationPath: null, // Use wallet's default signing key
            isPreHashed: false,
            cancellationToken);

        // Set sender wallet and raw signature bytes from wallet sign result
        transaction.SenderWallet = request.SenderWallet;
        transaction.Signature = signResult.Signature;

        // 12. Fetch next sequence number for replay protection (SEC-AUDIT 4.2) and submit.
        // Feature 108: submit to BOTH the local validator (seals iff this node is on the
        // roster) AND the peer-service fan-out (forwards to source peers for subscribed
        // registers — no-op when we own the register locally). No ownership branching here.
        var nextSeqNum = await _validatorClient.GetNextSequenceNumberAsync(
            instance.RegisterId, request.SenderWallet, cancellationToken);
        var submission = transaction.ToTransactionSubmission(signResult, nextSeqNum);

        // Feature 145 (T016/T034) — submit = local data-validation + mempool ingress AND a
        // carrier-aware fan-out, and NOTHING waits for sealing. The 202 attests "data-validated and
        // ingressed into the system"; the InstanceProjector advances every node when the docket seals
        // (which may be local — if this node is on the roster — or remote on a carrier; irrelevant to
        // the consumer). The fan-out targets ONLY peers that carry the register (no seed/topology
        // dial), so an unreachable non-carrier can no longer hang the submit, and there is no
        // LocallyOwned branch.
        var validatorResult = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);
        var distributeResult = _peerClient is null
            ? new DistributeTransactionResult(0, 0)
            : await DistributeSubmissionAsync(instance.RegisterId, submission, cancellationToken);

        // Rejected to the consumer only when the tx failed data-validation locally AND no carrier
        // accepted it. A subscriber's local validator does not seal (off-roster), so a carrier
        // acceptance is also a valid ingress signal. (Async sealing-validation failure feedback to
        // the consumer is a tracked follow-up — not surfaced here yet.)
        if (!validatorResult.Success && distributeResult.AcceptedCount == 0)
        {
            throw new InvalidOperationException(
                $"Transaction {transaction.TxId} rejected: [{validatorResult.ErrorCode}] {validatorResult.ErrorMessage}");
        }
        if (!validatorResult.Success)
        {
            _logger.LogWarning(
                "Local validator rejected transaction {TxId} for register {RegisterId} ([{ErrorCode}] {ErrorMessage}) " +
                "but a carrier accepted it ({AcceptedCount}/{TargetCount}) — ingressed, continuing",
                transaction.TxId, instance.RegisterId, validatorResult.ErrorCode, validatorResult.ErrorMessage,
                distributeResult.AcceptedCount, distributeResult.TargetPeerCount);
        }

        _logger.LogDebug(
            "Transaction {TxId} ingressed: validator={ValidatorSuccess}, carriers accepted={Accepted}/{Targets}",
            transaction.TxId, validatorResult.Success,
            distributeResult.AcceptedCount, distributeResult.TargetPeerCount);

        // 12b. Feature 145 — single async submission path. The submitter NEVER advances instance
        // state and NEVER waits for sealing; the InstanceProjector folds the sealed docket on every
        // node (SC-006). Always returns 202 (IsAsync); the caller observes advancement via the
        // instance read / hub event. Chain ordering is client-gated (wait on instance-advanced before
        // the next action — see contracts/submission-response.md).
        var confirmedTxId = transaction.TxId;
        await _actionStore.StoreIdempotencyKeyAsync(idempotencyKey, confirmedTxId, TimeSpan.FromHours(24));

        // Record an issued credential on its dedicated register if configured (FR-014c). This is the
        // one inline side effect retained on the submit path until the ReactionDispatcher (US2) owns it.
        if (issuedCredential != null && !string.IsNullOrEmpty(actionDef.CredentialIssuanceConfig?.RegisterId))
        {
            _logger.LogInformation(
                "Credential {CredentialId} of type {Type} issued from {Issuer} to {Recipient} (tx: {TxId})",
                issuedCredential.CredentialId, issuedCredential.Type,
                issuedCredential.IssuerDid, issuedCredential.SubjectDid, confirmedTxId);

            await RecordCredentialOnRegisterAsync(
                issuedCredential,
                actionDef.CredentialIssuanceConfig.RegisterId,
                request.SenderWallet,
                instanceId,
                confirmedTxId,
                cancellationToken);
        }

        var issuedCredentialResponse = issuedCredential is null
            ? null
            : await BuildIssuedCredentialResponseAsync(
                issuedCredential, blueprint, actionDef, caller, cancellationToken);

        _logger.LogInformation(
            "Action {ActionId} submitted for instance {InstanceId} (tx {TxId}); returning 202 — instance advances on projection of the sealed docket.",
            actionId, instanceId, confirmedTxId);

        return new ActionSubmissionResponse
        {
            TransactionId = confirmedTxId,
            InstanceId = instanceId,
            IsAsync = true,
            NextActions = [],
            IsComplete = false,
            Calculations = calculations,
            // Merge schema-validation warnings with any credential-claim warnings raised during issuance
            // (e.g. the portrait size gate dropping the image) so the submitter sees them (issue #340).
            Warnings = credentialWarnings.Count > 0
                ? (validationResult.Warnings ?? new List<string>()).Concat(credentialWarnings).ToList()
                : validationResult.Warnings,
            IssuedCredentialId = issuedCredential?.CredentialId,
            IssuedCredential = issuedCredentialResponse,
            CredentialOffer = haipOfferResult != null
                ? new HaipCredentialOfferResponse
                {
                    OfferId = haipOfferResult.OfferId,
                    CredentialOfferUri = haipOfferResult.CredentialOfferUri,
                    CredentialType = actionDef.CredentialIssuanceConfig?.CredentialType ?? string.Empty,
                    ExpiresAt = haipOfferResult.ExpiresAt
                }
                : null,
            // Presentation requests surface via the 202 Accepted short-circuit earlier in ExecuteAsync.
            PresentationRequest = null
        };
    }

    /// <inheritdoc/>
    public async Task<ActionRejectionResponse> RejectAsync(
        string instanceId,
        int actionId,
        ActionRejectionRequest request,
        string delegationToken,
        ClaimsPrincipal? caller = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("RejectAction");
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("action.id", actionId);

        _logger.LogInformation("Rejecting action {ActionId} for instance {InstanceId}", actionId, instanceId);

        // 1. Get the instance
        var instance = await _instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance == null)
        {
            throw new InvalidOperationException($"Instance {instanceId} not found");
        }

        // 2. Get the blueprint
        var blueprint = await _actionResolver.GetBlueprintAsync(instance.BlueprintId, instance.BlueprintDefinitionTxId, cancellationToken);
        if (blueprint == null)
        {
            throw new InvalidOperationException($"Blueprint {instance.BlueprintId} not found");
        }

        // 3. Get the action definition
        var actionDef = _actionResolver.GetActionDefinition(blueprint, actionId.ToString());
        if (actionDef == null)
        {
            throw new InvalidOperationException($"Action {actionId} not found in blueprint {blueprint.Id}");
        }

        // 4. Validate rejection is allowed for this action
        if (actionDef.RejectionConfig == null)
        {
            throw new InvalidOperationException($"Action {actionId} does not allow rejection");
        }

        // 5. Validate reason if required
        if (actionDef.RejectionConfig.RequireReason && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("Rejection reason is required for this action");
        }

        // 6. Get the target action
        var targetAction = _actionResolver.GetActionDefinition(blueprint, actionDef.RejectionConfig.TargetActionId.ToString());
        if (targetAction == null)
        {
            throw new InvalidOperationException($"Rejection target action {actionDef.RejectionConfig.TargetActionId} not found");
        }

        // 6b. Validate wallet ownership (SEC-006). A rejection is never an open-participant
        // walk-in (the rejecter is an established participant), so the profile is required.
        var rejectWallet = request.SenderWallet ?? instance.ParticipantWallets.Values.FirstOrDefault() ?? "";
        await ValidateWalletOwnershipAsync(rejectWallet, caller, allowMissingParticipant: false, cancellationToken);

        // 7. Build rejection transaction
        var rejectionData = new Dictionary<string, object>
        {
            ["rejectionReason"] = request.Reason,
            ["rejectedActionId"] = actionId,
            ["fieldErrors"] = request.FieldErrors ?? new Dictionary<string, string>()
        };

        var transaction = await _transactionBuilder.BuildRejectionTransactionAsync(
            blueprint,
            instance,
            actionDef,
            rejectionData,
            instance.LastTransactionId,
            cancellationToken);

        // 8. Sign and submit to Validator Service (using "{TxId}:{PayloadHash}" contract)
        var rejectSignResult = await _walletClient.SignTransactionAsync(
            request.SenderWallet ?? instance.ParticipantWallets.Values.FirstOrDefault() ?? "",
            transaction.SigningData,
            derivationPath: null,
            isPreHashed: false,
            cancellationToken);

        transaction.SenderWallet = request.SenderWallet ?? instance.ParticipantWallets.Values.FirstOrDefault() ?? "";
        transaction.Signature = rejectSignResult.Signature;

        var rejectSeqNum = await _validatorClient.GetNextSequenceNumberAsync(
            instance.RegisterId, transaction.SenderWallet, cancellationToken);
        var rejectSubmission = transaction.ToTransactionSubmission(rejectSignResult, rejectSeqNum);
        var rejectResult = await _validatorClient.SubmitTransactionAsync(rejectSubmission, cancellationToken);

        if (!rejectResult.Success)
        {
            throw new InvalidOperationException(
                $"Validator rejected transaction {transaction.TxId}: [{rejectResult.ErrorCode}] {rejectResult.ErrorMessage}");
        }

        // Poll for confirmation
        await WaitForTransactionConfirmationAsync(instance.RegisterId, transaction.TxId, cancellationToken);

        // 9. Update instance state
        if (actionDef.RejectionConfig.IsTerminal)
        {
            instance.State = InstanceState.Rejected;
            instance.CompletedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Route to target action
            instance.CurrentActionIds = [actionDef.RejectionConfig.TargetActionId];
        }
        instance.LastTransactionId = transaction.TxId;
        instance = await PersistInstanceAsync(instance, cancellationToken);

        // 10. Notify target participant via thin signal
        var targetParticipantId = actionDef.RejectionConfig.TargetParticipantId ?? targetAction.Sender;
        string? targetWalletAddress = null;
        instance.ParticipantWallets?.TryGetValue(targetParticipantId, out targetWalletAddress);
        await _notificationService.NotifyActionRejectedAsync(
            instanceId,
            targetWalletAddress,
            ct: cancellationToken);

        return new ActionRejectionResponse
        {
            TransactionId = transaction.TxId,
            InstanceId = instanceId,
            TargetAction = new TargetActionResponse
            {
                ActionId = targetAction.Id,
                ActionTitle = targetAction.Title,
                ParticipantId = targetParticipantId
            }
        };
    }

    private async Task<ValidationResult> ValidateActionDataAsync(
        ActionModel action,
        Dictionary<string, object> data,
        CancellationToken cancellationToken)
    {
        // Delegate to the Blueprint Engine for full JSON Schema validation
        if (action.DataSchemas?.Any() == true)
        {
            var engineResult = await _executionEngine.ValidateAsync(data, action, cancellationToken);
            return new ValidationResult
            {
                IsValid = engineResult.IsValid,
                Errors = engineResult.Errors.Select(e => e.Message).ToList(),
                Warnings = []
            };
        }

        // Fallback: field-presence check when no schemas are defined
        var errors = new List<string>();
        if (action.RequiredActionData?.Any() == true)
        {
            foreach (var required in action.RequiredActionData)
            {
                if (!data.ContainsKey(required))
                {
                    errors.Add($"Required field '{required}' is missing");
                }
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = []
        };
    }

    /// <inheritdoc />
    public async Task CompleteAfterPresentationAsync(
        string instanceId,
        int completedActionId,
        string outcomeTransactionId,
        IReadOnlyDictionary<string, object>? draftPayload,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("CompleteAfterPresentation");
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("action.id", completedActionId);
        activity?.SetTag("tx.id", outcomeTransactionId);

        _logger.LogInformation(
            "FR-015: completing action {ActionId} for instance {InstanceId} after presentation outcome tx {TxId}",
            completedActionId, instanceId, outcomeTransactionId);

        var instance = await _instanceStore.GetAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance {instanceId} not found");

        // NOTE: a downstream submission's state-reconstruction may race the
        // outcome tx's docket seal and chain off the wrong transaction (the
        // validator returns VAL_BP_003). That race is NOT fixed here — see
        // issue #582. Adding a confirmation wait at this point doesn't help
        // because the outcome tx's own previousTransactionId race
        // (VAL_CHAIN_001 against the not-yet-sealed presentation-initiated tx)
        // can prevent the outcome from ever sealing. Fixing the chain is
        // a Feature 111 design pass; this method advances the action's
        // lifecycle state idempotently regardless of validator-side outcome.

        if (!instance.CurrentActionIds.Contains(completedActionId))
        {
            // Idempotent replay — the action has already been advanced past this point
            // (e.g. duplicate callback racing the outcome write, or a manual replay).
            _logger.LogInformation(
                "Action {ActionId} on instance {InstanceId} is already not-current; skipping FR-015 advancement (idempotent replay)",
                completedActionId, instanceId);
            return;
        }

        var blueprint = await _actionResolver.GetBlueprintAsync(instance.BlueprintId, instance.BlueprintDefinitionTxId, cancellationToken)
            ?? throw new InvalidOperationException($"Blueprint {instance.BlueprintId} not found");

        var actionDef = _actionResolver.GetActionDefinition(blueprint, completedActionId.ToString())
            ?? throw new InvalidOperationException($"Action {completedActionId} not found in blueprint {blueprint.Id}");

        // Build mergedData from the draft payload only. State reconstruction would
        // require a delegation token to decrypt prior payloads — see XML doc on
        // IActionExecutionService.CompleteAfterPresentationAsync. Routes that don't
        // depend on prior decrypted state (the AssuredIdentity flow and any other
        // unconditional / payload-only-conditioned routing) work correctly with this
        // narrower context.
        var mergedData = draftPayload is null
            ? new Dictionary<string, object>()
            : draftPayload.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var calculations = await EvaluateCalculationsAsync(actionDef, mergedData, cancellationToken);
        if (calculations is not null)
        {
            foreach (var kvp in calculations)
            {
                mergedData[kvp.Key] = kvp.Value;
            }
        }

        var outputSource = BuildOutputMappingSource(
            payloadData: mergedData,
            calculations: calculations,
            haipOfferResult: null,
            actionDef: actionDef);

        var routingResult = await EvaluateRoutingAsync(blueprint, actionDef, mergedData, outputSource, cancellationToken);

        instance = await UpdateInstanceAfterExecutionAsync(
            instance,
            completedActionId,
            outcomeTransactionId,
            routingResult,
            cancellationToken);

        await NotifyParticipantsAsync(instance, actionDef, routingResult, cancellationToken);

        _logger.LogInformation(
            "FR-015: action {ActionId} on instance {InstanceId} completed; {NextCount} next action(s) routed; isComplete={IsComplete}",
            completedActionId, instanceId, routingResult.NextActions.Count, routingResult.NextActions.Count == 0);
    }

    /// <summary>
    /// Feature 145 US6 (<see cref="IPresentationRoutingDecisionBuilder"/>) — evaluate routing for a
    /// successful presentation outcome and return a sender-signed <see cref="RoutingDecision"/> for the
    /// outcome tx to carry, so the projector advances on its seal. Mirrors the routing computation of
    /// <see cref="CompleteAfterPresentationAsync"/> and the decision build/sign of the normal submit
    /// path (step 10d), but performs no advance. Returns null on a missing instance or a non-current
    /// action (idempotent replay), so no decision is attached and the deduplicated outcome tx does not
    /// re-advance the instance.
    /// </summary>
    public async Task<RoutingDecision?> BuildForPresentationOutcomeAsync(
        string instanceId,
        int completedActionId,
        IReadOnlyDictionary<string, object>? draftPayload,
        string submitterWallet,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("BuildPresentationRoutingDecision");
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("action.id", completedActionId);

        var instance = await _instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            _logger.LogWarning(
                "US6: instance {InstanceId} not found while building the presentation routing decision for action {ActionId}; no decision attached (the projector will not advance this outcome)",
                instanceId, completedActionId);
            return null;
        }

        if (!instance.CurrentActionIds.Contains(completedActionId))
        {
            // Idempotent replay — the presentation action has already advanced. Attach no decision so
            // the content-addressed (deduplicated) outcome tx does not re-advance the instance.
            _logger.LogInformation(
                "US6: action {ActionId} on instance {InstanceId} is no longer current; no presentation routing decision attached (idempotent replay)",
                completedActionId, instanceId);
            return null;
        }

        var blueprint = await _actionResolver.GetBlueprintAsync(instance.BlueprintId, instance.BlueprintDefinitionTxId, cancellationToken);
        if (blueprint is null)
        {
            _logger.LogWarning(
                "US6: blueprint {BlueprintId} not found for instance {InstanceId}; no presentation routing decision attached",
                instance.BlueprintId, instanceId);
            return null;
        }

        var actionDef = _actionResolver.GetActionDefinition(blueprint, completedActionId.ToString());
        if (actionDef is null)
        {
            _logger.LogWarning(
                "US6: action {ActionId} not found in blueprint {BlueprintId}; no presentation routing decision attached",
                completedActionId, blueprint.Id);
            return null;
        }

        // Routing context is the draft payload only (no prior-state decryption), exactly as the legacy
        // CompleteAfterPresentationAsync advance did — see its XML doc note.
        var mergedData = draftPayload is null
            ? new Dictionary<string, object>()
            : draftPayload.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var calculations = await EvaluateCalculationsAsync(actionDef, mergedData, cancellationToken);
        if (calculations is not null)
        {
            foreach (var kvp in calculations)
                mergedData[kvp.Key] = kvp.Value;
        }

        var outputSource = BuildOutputMappingSource(
            payloadData: mergedData, calculations: calculations, haipOfferResult: null, actionDef: actionDef);

        var routingResult = await EvaluateRoutingAsync(blueprint, actionDef, mergedData, outputSource, cancellationToken);

        var routingDecision = new RoutingDecision
        {
            CompletedActionId = completedActionId,
            NextActions = routingResult.NextActions
                .Select(n => new ActionRef { ActionId = n.ActionId, BranchKey = n.BranchId })
                .ToList(),
            // Feature 194: a presentation outcome advances the same instance as any other action, so
            // it carries the same pin. Omitting it here would leave one advancement path unpinned —
            // and the projector would then refuse it as a foreign decision.
            BlueprintDefinitionTxId = ResolveInstancePin(instance, completedActionId),
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned },
        };
        var routingSignResult = await _walletClient.SignTransactionAsync(
            submitterWallet, routingDecision.ComputeSignableBytes(),
            derivationPath: null, isPreHashed: false, cancellationToken);
        routingDecision.Attestation.Signature = Convert.ToBase64String(routingSignResult.Signature);

        _logger.LogInformation(
            "US6: built presentation routing decision for instance {InstanceId} action {ActionId} → {NextCount} next action(s)",
            instanceId, completedActionId, routingDecision.NextActions.Count);
        return routingDecision;
    }

    /// <summary>
    /// Feature 194 — the executable-definition hash to stamp on this action's routing decision:
    /// the instance's pin, established when the instance was created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is deliberately <b>no</b> "resolve the latest definition" branch here. Choosing a
    /// definition happens once, at instance creation; re-deriving it per action is precisely the
    /// defect this feature removes, and it would silently move an in-flight instance onto a newly
    /// published definition the moment anyone republished.
    /// </para>
    /// <para>
    /// An empty pin means the instance predates Feature 194 (or was created on a node with no
    /// published version resolvable). It is carried through as null so the decision serialises
    /// exactly as a pre-feature one would, and the fold takes the documented fallback — but it is
    /// logged, because an unpinned instance running on a pinned platform is worth an operator
    /// seeing rather than inferring.
    /// </para>
    /// </remarks>
    private string? ResolveInstancePin(Instance instance, int actionId)
    {
        if (!string.IsNullOrWhiteSpace(instance.BlueprintDefinitionTxId))
        {
            return instance.BlueprintDefinitionTxId;
        }

        _logger.LogWarning(
            "Instance {InstanceId} has no pinned blueprint definition; action {ActionId} will be " +
            "carried unpinned and folded via the pre-Feature-194 fallback (blueprint {BlueprintId}).",
            instance.Id, actionId, instance.BlueprintId);
        return null;
    }

    /// <summary>
    /// Feature 184 — resolves the non-sensitive reason code to carry on the signed routing decision.
    /// Returns null unless the taken route declares an <c>x-decision-notice</c> with a
    /// <c>reasonCodeField</c> that resolves against the submitted payload.
    /// </summary>
    /// <remarks>
    /// This is the ONLY point at which the reason is read from the payload. It runs on the deciding
    /// participant's node, which is submitting that payload and so plainly can read it. Everything
    /// downstream — including the recipient's node, which can not — works from the resulting code.
    /// </remarks>
    internal static string? ResolveDecisionReasonCode(
        ActionModel actionDef,
        string? matchedRouteId,
        IReadOnlyDictionary<string, object> mergedData)
    {
        if (string.IsNullOrEmpty(matchedRouteId) || actionDef.Routes is null)
            return null;

        var notice = actionDef.Routes
            .FirstOrDefault(r => string.Equals(r.Id, matchedRouteId, StringComparison.Ordinal))
            ?.DecisionNotice;

        if (notice is null || string.IsNullOrWhiteSpace(notice.ReasonCodeField))
            return null;

        return ResolvePointerString(mergedData, notice.ReasonCodeField);
    }

    /// <summary>
    /// Resolves a JSON Pointer against the merged action payload to a string, walking dictionary and
    /// <see cref="JsonElement"/> nodes. Returns null when the pointer is empty or unresolvable.
    /// </summary>
    private static string? ResolvePointerString(IReadOnlyDictionary<string, object> data, string? pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer))
            return null;

        var segments = pointer.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        object? current = data;

        foreach (var segment in segments)
        {
            switch (current)
            {
                case IReadOnlyDictionary<string, object> dict when dict.TryGetValue(segment, out var next):
                    current = next;
                    break;
                case JsonElement je when je.ValueKind == JsonValueKind.Object && je.TryGetProperty(segment, out var prop):
                    current = prop;
                    break;
                default:
                    return null;
            }
        }

        return current switch
        {
            null => null,
            string s => s,
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
            _ => current.ToString(),
        };
    }

    private async Task<RoutingResult> EvaluateRoutingAsync(
        BlueprintModel blueprint,
        ActionModel action,
        Dictionary<string, object> mergedData,
        JsonObject? outputSource,
        CancellationToken cancellationToken)
    {
        // Delegate to the Blueprint Engine for JSON Logic routing, passing the
        // output source document so Route.OutputMapping entries (if any) can be
        // evaluated in the same pass. Feature 104 wave 14a.
        var engineResult = await _executionEngine.DetermineRoutingWithMappingAsync(
            blueprint, action, mergedData, outputSource, cancellationToken);

        // Build action index once for O(1) lookups during mapping
        var actionIndex = Sorcha.Blueprint.Engine.BlueprintExtensions.BuildActionIndex(blueprint);

        // Map engine RoutedActions to service NextActions
        var nextActions = new List<NextAction>();

        foreach (var routedAction in engineResult.NextActions)
        {
            // Resolve action title from blueprint via O(1) index lookup
            ActionModel? targetActionDef = null;
            if (int.TryParse(routedAction.ActionId, out var targetId))
                actionIndex.TryGetValue(targetId, out targetActionDef);

            nextActions.Add(new NextAction
            {
                ActionId = int.TryParse(routedAction.ActionId, out var id) ? id : 0,
                ActionTitle = targetActionDef?.Title ?? "",
                ParticipantId = routedAction.ParticipantId ?? targetActionDef?.Sender ?? "",
                BranchId = routedAction.BranchId
            });
        }

        return new RoutingResult
        {
            NextActions = nextActions,
            IsParallel = engineResult.IsParallel,
            MatchedRouteId = engineResult.MatchedRouteId,
            PendingPayloads = engineResult.PendingPayloads
        };
    }

    /// <summary>
    /// Builds the output source JsonObject for <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/>
    /// evaluation. Shape: <c>{ "payload": {...}, "calculations": {...}, "haip": {...}? }</c>.
    /// The <c>haip</c> key is present only when the current action minted an
    /// OpenID4VCI credential offer via the HAIP service (Feature 104 wave 14b).
    /// </summary>
    private static JsonObject BuildOutputMappingSource(
        IReadOnlyDictionary<string, object> payloadData,
        IReadOnlyDictionary<string, object>? calculations,
        CreateOfferResult? haipOfferResult = null,
        ActionModel? actionDef = null)
    {
        var source = new JsonObject();

        // Serialise payload and calculations through System.Text.Json so we
        // consistently get JsonNode values regardless of the input object types.
        source["payload"] = ConvertToJsonNode(payloadData) ?? new JsonObject();
        source["calculations"] = ConvertToJsonNode(calculations ?? new Dictionary<string, object>()) ?? new JsonObject();

        if (haipOfferResult is not null)
        {
            // Expose the HAIP mint output under /haip/* so the Verified Citizen v2
            // blueprint can declare routes like
            //   "outputMapping": {
            //     "/haip/credential_offer_uri": "/credentialOffer/credential_offer_uri",
            //     "/haip/credential_type":      "/credentialOffer/credential_type",
            //     "/haip/expires_at":           "/credentialOffer/expires_at"
            //   }
            // and carry the offer into the claim action's prepopulated payload.
            //
            // Human-readable display strings (title / subtitle / description /
            // issuer name) are the blueprint author's responsibility — they ship
            // as literals on the claim action's schema defaults so they can be
            // localised per blueprint. The service only emits protocol fields.
            //
            // Defensive: if the HAIP service returned a result with an empty
            // credential_offer_uri, that's a contract violation — the downstream
            // claim action would silently fall through to the default form
            // renderer (via the resolver's IsNullOrWhiteSpace guard) with no
            // user-facing error. Throw loudly instead.
            if (string.IsNullOrWhiteSpace(haipOfferResult.CredentialOfferUri))
            {
                throw new InvalidOperationException(
                    "HAIP service returned a credential offer with an empty CredentialOfferUri. " +
                    "This indicates a contract violation in the HAIP service — downstream claim " +
                    "actions cannot render without the offer URI.");
            }

            var haipNode = new JsonObject
            {
                ["credential_offer_uri"] = haipOfferResult.CredentialOfferUri,
                ["offer_id"] = haipOfferResult.OfferId.ToString(),
                ["expires_at"] = haipOfferResult.ExpiresAt.ToString("O")
            };

            var issuanceConfig = actionDef?.CredentialIssuanceConfig;
            if (issuanceConfig is not null)
            {
                haipNode["credential_type"] = issuanceConfig.CredentialType;
            }

            source["haip"] = haipNode;
        }

        return source;
    }

    /// <summary>
    /// Converts a loosely-typed .NET dictionary/value (as produced by the
    /// routing pipeline) into a <see cref="JsonNode"/> tree suitable for
    /// JSON Pointer traversal by the output-mapping evaluator.
    /// </summary>
    /// <remarks>
    /// Failure modes are non-fatal: a value that cannot round-trip through
    /// System.Text.Json (circular references, unsupported types) or produces
    /// invalid JSON results in a null return and a logged warning rather than
    /// aborting the action execution. The caller substitutes an empty
    /// <see cref="JsonObject"/> so routing proceeds with an absent source —
    /// which simply causes any <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/>
    /// entries pointing into the unconvertible branch to be silently skipped,
    /// the same as if the data were not present.
    /// </remarks>
    private static JsonNode? ConvertToJsonNode(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            // Round-trip through System.Text.Json — slower than a hand-rolled
            // converter but handles nested dicts, lists, JsonElements, primitives,
            // and records uniformly.
            var json = JsonSerializer.Serialize(value);
            return JsonNode.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Preserve the engine's ability to proceed when a single value in the
            // source document can't be serialised. OutputMapping entries that
            // reference the affected subtree will resolve as absent and be silently
            // skipped per FR-004.
            return null;
        }
    }

    private async Task<Dictionary<string, object>?> EvaluateCalculationsAsync(
        ActionModel action,
        Dictionary<string, object> mergedData,
        CancellationToken cancellationToken)
    {
        if (action.Calculations == null || action.Calculations.Count == 0)
        {
            return null;
        }

        try
        {
            // Delegate to the Blueprint Engine for JSON Logic calculation evaluation
            var result = await _executionEngine.ApplyCalculationsAsync(mergedData, action, cancellationToken);

            // Return only the calculated fields (those defined in action.Calculations)
            var calculations = new Dictionary<string, object>();
            foreach (var fieldName in action.Calculations.Keys)
            {
                if (result.TryGetValue(fieldName, out var value))
                {
                    calculations[fieldName] = value;
                }
            }

            return calculations.Count > 0 ? calculations : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate calculations for action {ActionId}", action.Id);
            return null;
        }
    }

    // Feature 176: disclosure resolution moved to the shared IActionDisclosureResolver so the execution
    // path and the disclosed-data query endpoint share one authority (no behaviour fork). This thin
    // delegation preserves the original call shape at the single call site (step 9b).
    private Task<Dictionary<string, Dictionary<string, object>>> ApplyDisclosuresAsync(
        ActionModel action,
        Dictionary<string, object> data,
        BlueprintModel blueprint,
        Dictionary<string, string> participantWallets,
        string registerId)
        => _actionDisclosureResolver.ApplyDisclosuresAsync(
            action, data, blueprint, participantWallets, registerId);

    /// <summary>
    /// Evaluates the optional credential <c>issuanceCondition</c> (Feature 176 / FR-004) over the
    /// submitted action data. Returns true (issue) when no condition is configured — the pre-existing
    /// always-issue behaviour. Returns false (skip issuance) when the condition evaluates falsy, and
    /// fails closed (false) when a configured condition cannot be evaluated.
    /// </summary>
    private bool EvaluateIssuanceCondition(ActionModel actionDef, Dictionary<string, object> data)
    {
        var condition = actionDef.CredentialIssuanceConfig?.IssuanceCondition;
        if (condition is null)
        {
            return true;
        }

        if (_jsonLogicEvaluator is null)
        {
            _logger.LogError(
                "Action {ActionId} declares a credential issuanceCondition but no JSON Logic evaluator is "
                + "available; failing closed and NOT issuing a credential.", actionDef.Id);
            return false;
        }

        try
        {
            var result = _jsonLogicEvaluator.Evaluate(condition, data);
            var allowed = IsConditionTruthy(result);
            if (!allowed)
            {
                _logger.LogInformation(
                    "Action {ActionId}: credential issuanceCondition evaluated falsy — no credential minted or delivered.",
                    actionDef.Id);
            }

            return allowed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Action {ActionId}: credential issuanceCondition evaluation threw — failing closed, no credential issued.",
                actionDef.Id);
            return false;
        }
    }

    /// <summary>JSON-Logic truthiness for the issuance-condition result (mirrors jsonlogic.com semantics).</summary>
    private static bool IsConditionTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrEmpty(s) && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        decimal m => m != 0m,
        System.Text.Json.Nodes.JsonValue jv when jv.TryGetValue<bool>(out var jb) => jb,
        _ => true,
    };

    private const int MaxConcurrencyRetries = 3;

    private async Task<Instance> UpdateInstanceAfterExecutionAsync(
        Instance instance,
        int completedActionId,
        string transactionId,
        RoutingResult routingResult,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= MaxConcurrencyRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Re-read the instance on retry to get latest version
                _logger.LogWarning(
                    "Concurrency conflict on instance {InstanceId}, retry {Attempt}/{Max}",
                    instance.Id, attempt, MaxConcurrencyRetries);

                instance = (await _instanceStore.GetAsync(instance.Id, cancellationToken))!;
            }

            ApplyInstanceStateChanges(instance, completedActionId, transactionId, routingResult);

            try
            {
                return await PersistInstanceAsync(instance, cancellationToken);
            }
            catch (ConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // Retry with fresh state
            }
        }

        // Should not reach here, but satisfy compiler
        throw new InvalidOperationException(
            $"Failed to update instance {instance.Id} after {MaxConcurrencyRetries} retries due to concurrent modifications");
    }

    /// <summary>
    /// Persists instance state changes for the presentation-completion path (US6), which still
    /// advances the instance imperatively. Feature 145 removed the read-only-mirror split — there is
    /// one writer per row and the normal action path advances via the <c>InstanceProjector</c>, so
    /// this is now a plain <see cref="IInstanceStore.UpdateAsync"/> under optimistic concurrency.
    /// </summary>
    private Task<Instance> PersistInstanceAsync(Instance instance, CancellationToken cancellationToken)
        => _instanceStore.UpdateAsync(instance, cancellationToken);

    private static void ApplyInstanceStateChanges(
        Instance instance,
        int completedActionId,
        string transactionId,
        RoutingResult routingResult)
    {
        // Remove completed action from current actions
        instance.CurrentActionIds.Remove(completedActionId);

        // Remove the consumed seed payload (if any) atomically with the
        // state change. Feature 104 wave 14a (FR-008).
        instance.PendingActionPayloads.Remove(completedActionId);

        // Add next actions
        foreach (var nextAction in routingResult.NextActions)
        {
            if (!instance.CurrentActionIds.Contains(nextAction.ActionId))
            {
                instance.CurrentActionIds.Add(nextAction.ActionId);
            }

            // Track parallel branches
            if (!string.IsNullOrEmpty(nextAction.BranchId))
            {
                if (!instance.ActiveBranches.Any(b => b.Id == nextAction.BranchId))
                {
                    instance.ActiveBranches.Add(new Branch
                    {
                        Id = nextAction.BranchId,
                        CurrentActionId = nextAction.ActionId,
                        State = BranchState.Active
                    });
                }
            }
        }

        // Seed prepopulated payloads for next actions from Route.OutputMapping
        // evaluation (if any). Feature 104 wave 14a (FR-002, FR-003).
        if (routingResult.PendingPayloads is { Count: > 0 } pendingPayloads)
        {
            foreach (var (actionId, payload) in pendingPayloads)
            {
                if (payload is null || payload.Count == 0)
                {
                    continue;
                }
                // Deep clone so the engine's transient object is insulated
                // from any subsequent mutation of the persisted seed.
                var cloned = JsonNode.Parse(payload.ToJsonString()) as JsonObject;
                if (cloned is null)
                {
                    continue;
                }
                instance.PendingActionPayloads[actionId] = cloned;
            }
        }

        // Update transaction tracking
        instance.LastTransactionId = transactionId;
        instance.CompletedActionCount++;

        if (instance.FirstTransactionId == null)
        {
            instance.FirstTransactionId = transactionId;
        }

        // Check if workflow is complete
        if (instance.CurrentActionIds.Count == 0)
        {
            instance.State = InstanceState.Completed;
            instance.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task NotifyParticipantsAsync(
        Instance instance,
        ActionModel completedAction,
        RoutingResult routingResult,
        CancellationToken cancellationToken)
    {
        foreach (var nextAction in routingResult.NextActions)
        {
            // Resolve participant wallet address for direct notification
            string? walletAddress = null;
            instance.ParticipantWallets?.TryGetValue(nextAction.ParticipantId, out walletAddress);

            await _notificationService.NotifyActionAvailableAsync(
                instance.Id,
                walletAddress,
                actionId: nextAction.ActionId.ToString(),
                ct: cancellationToken);
        }

        if (routingResult.NextActions.Count == 0)
        {
            // Collect all participant wallet addresses for workflow completion signal
            var walletAddresses = instance.ParticipantWallets?.Values
                .Where(w => !string.IsNullOrEmpty(w))
                .Distinct()
                ?? [];

            await _notificationService.NotifyWorkflowCompletedAsync(
                instance.Id,
                walletAddresses!,
                cancellationToken);
        }
    }

    private async Task WaitForTransactionConfirmationAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _confirmationOptions.Timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var confirmedTx = await _registerClient.GetTransactionAsync(
                registerId, txId, cancellationToken);

            if (confirmedTx != null)
            {
                _logger.LogInformation(
                    "Transaction {TxId} confirmed in docket {DocketNumber} for register {RegisterId}",
                    txId, confirmedTx.DocketNumber, registerId);
                return;
            }

            await Task.Delay(_confirmationOptions.PollInterval, cancellationToken);
        }

        throw new TimeoutException(
            $"Transaction {txId} was not confirmed within {_confirmationOptions.Timeout.TotalSeconds}s for register {registerId}");
    }

    /// <summary>
    /// Applies the credential issuance <c>ClaimMappings</c> list against the
    /// action's merged data, walking JSON Pointer <c>SourceField</c> paths so
    /// nested primitive values (Feature 103) resolve correctly. Shared by
    /// both the internal issuance path and the HAIP external-wallet path.
    /// Missing mappings are logged at Warning level because a dropped claim
    /// silently produces a credential with fewer attributes than expected.
    /// </summary>
    private Dictionary<string, object?> BuildClaimsFromMappings(
        IEnumerable<Sorcha.Blueprint.Models.Credentials.ClaimMapping>? mappings,
        IReadOnlyDictionary<string, object?> mergedData,
        ICollection<string>? warnings = null)
        => BuildClaimsFromMappings(mappings, mergedData, _logger, warnings);

    /// <summary>
    /// Static logger-injected overload used by unit tests so the helper can
    /// be exercised without needing the full <see cref="ActionExecutionService"/>
    /// constructor graph. When <paramref name="warnings"/> is supplied, a
    /// client-facing message is appended for each claim the server drops (e.g. an
    /// oversized portrait) so the caller can surface it on the submission response
    /// instead of the drop being visible only in the server log (issue #340).
    /// </summary>
    internal static Dictionary<string, object?> BuildClaimsFromMappings(
        IEnumerable<Sorcha.Blueprint.Models.Credentials.ClaimMapping>? mappings,
        IReadOnlyDictionary<string, object?> mergedData,
        ILogger logger,
        ICollection<string>? warnings = null)
    {
        var claims = new Dictionary<string, object?>();
        if (mappings is null) return claims;

        foreach (var mapping in mappings)
        {
            if (TryResolveJsonPointer(mergedData, mapping.SourceField, out var value))
            {
                // Feature 107 — server is the authoritative size gate for
                // embedded portrait token images. A base64 string >27_000
                // chars means either the client-side resizer was bypassed
                // or the source photo is too detailed; either way the
                // credential should not carry an oversized claim. Drop the
                // claim, log the warning, and continue with the rest of
                // the credential so issuance does not abort.
                if (IsPortraitTokenMapping(mapping.SourceField) &&
                    value is string portraitBase64 &&
                    portraitBase64.Length > PortraitTokenMaxBase64Chars)
                {
                    logger.LogWarning(
                        "Portrait token for claim '{ClaimName}' exceeded the {MaxChars}-char base64 bound " +
                        "({ActualChars} chars); dropping claim and issuing credential without portrait. " +
                        "Warning code: {WarningCode}",
                        mapping.ClaimName, PortraitTokenMaxBase64Chars, portraitBase64.Length,
                        ValidationWarningCodes.CredentialPortraitOversize);
                    warnings?.Add(
                        $"The portrait image for '{mapping.ClaimName}' exceeded the " +
                        $"{PortraitTokenMaxBase64Chars:N0}-character limit and was omitted from the credential " +
                        $"({ValidationWarningCodes.CredentialPortraitOversize}). Re-submit with a smaller image to include it.");
                    continue;
                }

                claims[mapping.ClaimName] = value;
            }
            else
            {
                // Note: the walker treats both "key missing" and "key present
                // with null value" as unresolvable. The log message uses the
                // neutral "no value at" phrasing so it's accurate for both
                // cases. A null optional field (e.g. middleName) will produce
                // this warning and a credential without that claim, which is
                // the correct behaviour for issuance.
                logger.LogWarning(
                    "Claim mapping source '{SourceField}' has no value in action data; dropping claim '{ClaimName}' from credential",
                    mapping.SourceField, mapping.ClaimName);
            }
        }
        return claims;
    }

    /// <summary>
    /// Bound from <c>specs/107-assured-identity-v1/contracts/portrait-claim-format.md</c>:
    /// a 240×320 JPEG at the token spec's 20KB raw target produces ~27KB
    /// when base64-encoded (raw × 4/3 ≈ 1.333, plus up to two padding chars
    /// and occasional line breaks — call it ~27KB to keep a small headroom).
    /// The gate applies to the base64-encoded length, which is what actually
    /// ships in the SD-JWT.
    /// </summary>
    private const int PortraitTokenMaxBase64Chars = 27_000;

    /// <summary>
    /// Treats any claim mapping whose source pointer ends in
    /// <c>/tokenImageBase64</c> as a portrait token subject to the size
    /// gate. This ties the gate to schema convention rather than claim
    /// name, so future credential types that embed tokenised images reuse
    /// the gate without any change here.
    /// </summary>
    private static bool IsPortraitTokenMapping(string sourceField) =>
        sourceField.EndsWith("/tokenImageBase64", StringComparison.Ordinal);

    /// <summary>
    /// Resolves a JSON Pointer (<c>/foo/bar/baz</c>) against a root dictionary
    /// built from the action payload. Walks nested <see cref="Dictionary{TKey,TValue}"/>,
    /// <see cref="IDictionary{TKey,TValue}"/>, and <see cref="JsonElement"/> nodes.
    /// Returns <c>false</c> on any missing segment.
    /// </summary>
    /// <remarks>
    /// Used by the claim-mapping extractor so that primitive-nested payloads
    /// (e.g. <c>/name/givenName</c> for a PersonName/v1-backed submission)
    /// resolve correctly. Flat paths (<c>/givenName</c>) continue to work
    /// because the pointer walk of a single segment degenerates to a
    /// top-level lookup. RFC 6901 escape sequences (<c>~1</c> → <c>/</c>,
    /// <c>~0</c> → <c>~</c>) are unescaped per segment. Feature 103 US2/US4.
    ///
    /// <para><b>Deviations from RFC 6901:</b> the empty pointer <c>""</c>
    /// (whole document) and the single-slash pointer <c>"/"</c> (key
    /// <c>""</c>) are both treated as unresolvable and return <c>false</c>.
    /// Neither has a use case for credential claim mapping, and conflating
    /// them is the simpler contract for the call sites. Explicit-null
    /// values are also treated as unresolvable so the issuance path drops
    /// the claim rather than emitting a credential with a null attribute.</para>
    /// </remarks>
    internal static bool TryResolveJsonPointer(
        IReadOnlyDictionary<string, object?> root,
        string jsonPointer,
        out object? value)
    {
        value = null;
        if (string.IsNullOrEmpty(jsonPointer) || jsonPointer == "/")
        {
            return false;
        }

        // RFC 6901: ~1 decodes to /, ~0 decodes to ~. Order matters:
        // ~1 must be handled BEFORE ~0 to avoid double-unescape.
        var segments = jsonPointer
            .TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = segments[i].Replace("~1", "/").Replace("~0", "~");
        }

        if (segments.Length == 0) return false;

        // First segment: look up in the root dictionary.
        if (!root.TryGetValue(segments[0], out var current) || current is null)
        {
            return false;
        }

        // Subsequent segments: descend through nested structures.
        for (var i = 1; i < segments.Length; i++)
        {
            current = DescendOneLevel(current, segments[i]);
            if (current is null) return false;
        }

        value = current;
        return true;
    }

    private static object? DescendOneLevel(object parent, string key)
    {
        switch (parent)
        {
            case IDictionary<string, object?> nullableDict:
                return nullableDict.TryGetValue(key, out var v1) ? v1 : null;

            case System.Collections.IDictionary plainDict:
                return plainDict.Contains(key) ? plainDict[key] : null;

            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                return element.TryGetProperty(key, out var child) ? (object)child : null;

            default:
                return null;
        }
    }

    private async Task<CredentialIssuanceResult?> IssueCredentialFromActionAsync(
        ActionModel actionDef,
        Dictionary<string, object> mergedData,
        string senderWallet,
        Instance instance,
        string? issuerOrgName,
        string? issuerTenantId,
        JsonElement? holderJwk,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var config = actionDef.CredentialIssuanceConfig!;

        // Map claims from action data using ClaimMappings.
        //
        // Feature 103: sourceField is a JSON Pointer, so it may be either
        // flat ("/givenName") or nested into a primitive value object
        // ("/name/givenName" where name is a PersonName/v1 reference). The
        // extraction walks the pointer segment-by-segment through nested
        // dictionaries and JsonElement objects. Missing segments log a
        // warning and skip the claim rather than failing the whole issue.
        var claims = BuildClaimsFromMappings(config.ClaimMappings, mergedData!, warnings);

        // Derive age_over_NN booleans from dateOfBirth for any age-threshold claim the blueprint
        // maps (e.g. { "claimName": "age_over_18", "sourceField": "/dob/dateOfBirth" }). Issuing the
        // boolean instead of the raw date is the EUDI/ISO 18013-5 minimal-disclosure pattern the
        // verifier "Age over 18?" preset matches. Fail-closed: if the DOB is missing/unparseable the
        // claim is omitted rather than defaulted.
        var ageToday = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var mapping in config.ClaimMappings)
        {
            if (!AgeClaimDeriver.AgeOverClaimThreshold(mapping.ClaimName, out var threshold))
                continue;

            var dobString = TryResolveJsonPointer(mergedData!, mapping.SourceField, out var dobValue)
                ? dobValue?.ToString()
                : null;

            if (AgeClaimDeriver.TryDeriveAgeOver(dobString, ageToday, threshold, out var isOver))
            {
                claims[mapping.ClaimName] = isOver;
            }
            else
            {
                claims.Remove(mapping.ClaimName);   // drop the raw-date copy BuildClaimsFromMappings made
                warnings.Add($"[WARN_CRED_AGE_DERIVE] {mapping.ClaimName}: dateOfBirth missing or unparseable; claim omitted.");
            }
        }
        // Wallet client expects non-nullable values. Safe because
        // TryResolveJsonPointer returns false on null-valued segments —
        // BuildClaimsFromMappings never produces a null value. If that
        // invariant is ever relaxed, this projection must filter or
        // coerce nulls before the wire call.
        var claimsForWallet = claims.ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

        // Resolve recipient wallet address from participant ID
        var recipientWallet = senderWallet; // Default: issuer is also recipient
        if (!string.IsNullOrEmpty(config.RecipientParticipantId))
        {
            if (instance.ParticipantWallets.TryGetValue(config.RecipientParticipantId, out var wallet))
            {
                recipientWallet = wallet;
            }
            else
            {
                _logger.LogWarning(
                    "Recipient participant {ParticipantId} not found in instance wallets — credential will be issued to sender",
                    config.RecipientParticipantId);
            }
        }

        // Feature 093 US2: allocate the status list index BEFORE signing so the signed
        // credential payload can carry a valid credentialStatus pointer. Allocation uses a
        // synthetic credential identifier for the in-memory list position; the Wallet Service
        // generates the final credential ID at signing time. The bit position is unique per
        // allocation regardless of the log identifier.
        //
        // KNOWN FOLLOW-UP: tracked as Sorcha-Platform/Sorcha#220. If a future
        // IStatusListManager implementation starts keying lookups by the credential ID
        // passed here (rather than by listId + index), the "pending-{GUID}" placeholder
        // will cause revocation lookups to fail. The current in-memory StatusListManager
        // uses (listId, index) as its only key so this is a non-issue today, but should
        // be reconciled when spec 095 lands a persistent backing store.
        //
        // The CredentialStatus:EnableEmbedding flag (default true) lets pure-internal dev
        // environments disable the allocation step — useful when the Blueprint Service is
        // running without a status list manager wired up.
        string? preAllocatedStatusListUrl = null;
        int? preAllocatedStatusListIndex = null;
        // W3C treats revocation (not reversible) and suspension (reversible) as different
        // statuses, so the credential carries one entry per purpose at the same index.
        string? preAllocatedSuspensionListUrl = null;

        if (_statusListManager != null && _credentialStatusEmbeddingEnabled)
        {
            try
            {
                // #220: allocation is keyed by (listId, index), not by credential id — and the real
                // urn:uuid: id doesn't exist until the wallet signs the credential below. Pass null
                // rather than a synthetic "pending-{guid}" so a future persistent status-list store
                // can't mistake the placeholder for a real credential key.
                var allocation = await _statusListManager.AllocateIndexAsync(
                    senderWallet, instance.RegisterId, credentialId: null, cancellationToken);
                preAllocatedStatusListUrl = allocation.StatusListUrl;
                preAllocatedStatusListIndex = allocation.Index;
                preAllocatedSuspensionListUrl = allocation.SuspensionListUrl;

                _logger.LogInformation(
                    "Pre-allocated status list index {Index} in list {ListId} for upcoming credential issuance",
                    allocation.Index, allocation.ListId);
            }
            catch (Exception ex)
            {
                // Round 3 fix: when EnableEmbedding is true (default), allocation
                // failure now fails the action rather than silently issuing a credential
                // without the embedded claim. The previous non-fatal fallback produced
                // HAIP-non-compliant credentials that *appeared* compliant — exactly
                // the silent-degradation pattern this spec is meant to close. Operators
                // who do not want fail-closed behaviour can set
                // CredentialStatus:EnableEmbedding=false in Blueprint Service config to
                // skip allocation entirely (dev-environment escape hatch).
                _logger.LogError(ex,
                    "Failed to pre-allocate status list index for Blueprint action {ActionId} — failing the action because CredentialStatus:EnableEmbedding=true",
                    actionDef.Id);
                throw new InvalidOperationException(
                    "Status list allocation failed during credential issuance. " +
                    "Set CredentialStatus:EnableEmbedding=false in Blueprint Service config " +
                    "to issue credentials without embedded status claims (dev environments only).",
                    ex);
            }
        }

        try
        {
            _logger.LogDebug(
                "Issuing {CredentialType} with {ClaimCount} claims: [{ClaimNames}]",
                config.CredentialType, claims.Count, string.Join(", ", claims.Keys));

            var result = await _walletClient.IssueCredentialAsync(
                issuerWalletAddress: senderWallet,
                credentialType: config.CredentialType,
                claims: claimsForWallet,
                recipientWallet: recipientWallet,
                expiryDuration: config.ExpiryDuration,
                disclosableClaims: config.Disclosable?.ToList(),
                issuanceBlueprintId: instance.BlueprintId,
                statusListUrl: preAllocatedStatusListUrl,
                statusListIndex: preAllocatedStatusListIndex,
                statusListPurpose: preAllocatedStatusListUrl != null ? "revocation" : null,
                suspensionStatusListUrl: preAllocatedSuspensionListUrl,
#pragma warning disable CS0618 // accept deprecated SorchaInternal for backward-compat (treated as local-wallet delivery)
                skipRecipientStore: config.TargetAudience is TargetAudience.SorchaLocalWallet or TargetAudience.SorchaInternal,
#pragma warning restore CS0618
                issuerOrgName: issuerOrgName,
                tenantId: issuerTenantId,
                holderJwk: holderJwk,
                // Feature 181 US4 — carry the credential's X.509 trust anchor so the Wallet Service attaches
                // the imported external chain (x509-lotl, fail-closed). register/x509-tenant pass null to
                // preserve the exact pre-181 chain-attach behaviour (FR-021): tenant chain when a tenant id
                // is present, DID-only otherwise.
                trustAnchor: config.TrustAnchor == Sorcha.Blueprint.Models.Credentials.TrustAnchor.X509Lotl
                    ? "x509-lotl"
                    : null,
                // Credential VCT decoupling — carry the canonical vct URI and the authored
                // display name so the issuer stamps the citizen entity's Type with the vct
                // and records credentialName in the display config (not the bare type).
                vct: config.Vct,
                displayName: config.DisplayName,
                // The register this credential is issued against, persisted on the ISSUER's row.
                // The credential-lifecycle endpoints read that row and need it to post a
                // CredentialStatusChange transaction; without it a revocation never reaches the
                // ledger, so it cannot be audited or replicated to another node (#1482).
                registerId: config.RegisterId ?? instance.RegisterId,
                cancellationToken: cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to issue credential of type {CredentialType} for action {ActionId}",
                config.CredentialType, actionDef.Id);
            // Credential issuance failure is non-fatal — the action still succeeds
            return null;
        }
    }

    /// <summary>
    /// Feature 137 / C3 — resolves the recipient's carried delivery keys from reconstructed
    /// instance state. <paramref name="holderJwkPointer"/> (e.g. <c>/holderKeys/holderJwk</c>)
    /// locates the holder JWK for the SD-JWT <c>cnf</c> binding; the sibling
    /// <c>encryptionPublicKey</c> + <c>algorithm</c> are read from the same parent object and feed
    /// the on-register AEAD envelope wrap. All values are public material written by a
    /// <c>sorcha-holder-key</c> form field on the starting action. Any segment that does not
    /// resolve is returned as null.
    /// </summary>
    internal static (JsonElement? HolderJwk, string? EncryptionPublicKey, string? Algorithm) ResolveCarriedHolderKeys(
        Dictionary<string, object> mergedData,
        string holderJwkPointer)
    {
        JsonElement? holderJwk = null;
        if (TryResolveJsonPointer(mergedData!, holderJwkPointer, out var jwkValue) && jwkValue is not null)
        {
            // Re-serialise to a JsonElement regardless of whether the reconstructed value is a
            // JsonElement (register-sourced) or a Dictionary (instance-stored fallback).
            holderJwk = JsonSerializer.SerializeToElement(jwkValue);
        }

        var lastSlash = holderJwkPointer.LastIndexOf('/');
        var parentPointer = lastSlash > 0 ? holderJwkPointer[..lastSlash] : string.Empty;

        var encryptionPublicKey = TryResolveJsonPointer(mergedData!, $"{parentPointer}/encryptionPublicKey", out var encValue)
            ? CoercePointerValueToString(encValue)
            : null;
        var algorithm = TryResolveJsonPointer(mergedData!, $"{parentPointer}/algorithm", out var algValue)
            ? CoercePointerValueToString(algValue)
            : null;

        return (
            holderJwk,
            string.IsNullOrWhiteSpace(encryptionPublicKey) ? null : encryptionPublicKey,
            string.IsNullOrWhiteSpace(algorithm) ? null : algorithm);
    }

    /// <summary>
    /// Coerces a JSON-Pointer-resolved value to its string form. Handles raw strings,
    /// <see cref="JsonElement"/> string nodes, and null/undefined nodes (returned as null).
    /// </summary>
    private static string? CoercePointerValueToString(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
        JsonElement je when je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonElement je => je.ToString(),
        _ => value.ToString()
    };

    private async Task RecordCredentialOnRegisterAsync(
        CredentialIssuanceResult credential,
        string registerId,
        string senderWallet,
        string instanceId,
        string actionTransactionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build a credential-issuance record transaction for the dedicated credential register
            var credentialRecordPayload = new
            {
                type = "credential-issuance",
                credentialId = credential.CredentialId,
                credentialType = credential.Type,
                issuer = credential.IssuerDid,
                recipient = credential.SubjectDid,
                issuedAt = credential.IssuedAt,
                expiresAt = credential.ExpiresAt,
                actionTransactionId,
                instanceId,
                timestamp = DateTimeOffset.UtcNow
            };

            // Serialize with canonical options for deterministic hashing
            var transactionData = JsonSerializer.SerializeToUtf8Bytes(
                credentialRecordPayload, TransactionBuilderServiceExtensions.CanonicalJsonOptions);
            var hashBytes = System.Security.Cryptography.SHA256.HashData(transactionData);
            var txId = Convert.ToHexString(hashBytes).ToLowerInvariant();

            // PayloadHash = TxId — same canonical bytes, same hash
            var payloadHash = txId;

            var credTransaction = new BuiltTransaction
            {
                TransactionData = transactionData,
                TxId = txId,
                PayloadHash = payloadHash,
                TransactionType = "credential-issuance",
                RegisterId = registerId,
                Metadata = new Dictionary<string, object>
                {
                    ["blueprintId"] = instanceId,
                    ["actionId"] = 0,
                    ["instanceId"] = instanceId,
                    ["previousTxId"] = actionTransactionId,
                    // Feature 155 — these reach the sealed TransactionMetaData.TrackingData via the
                    // ToTransactionSubmission whitelist so the public anchor endpoint
                    // (GET /api/registers/{registerId}/credentials/{credentialId}/anchor) can locate
                    // the issuance tx by TrackingData["type"]=="credential-issuance" AND
                    // TrackingData["credentialId"]==<id>.
                    ["type"] = "credential-issuance",
                    ["credentialId"] = credential.CredentialId,
                    ["credentialType"] = credential.Type
                }
            };

            // Sign using "{TxId}:{PayloadHash}" contract (matches Validator verification)
            var signResult = await _walletClient.SignTransactionAsync(
                senderWallet,
                credTransaction.SigningData,
                derivationPath: null,
                isPreHashed: false,
                cancellationToken);

            credTransaction.SenderWallet = senderWallet;
            credTransaction.Signature = signResult.Signature;

            // Submit to the Validator for the credential register
            var submission = credTransaction.ToTransactionSubmission(signResult);
            var result = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Credential {CredentialId} recorded on register {RegisterId} (tx: {TxId})",
                    credential.CredentialId, registerId, txId);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to record credential {CredentialId} on register {RegisterId}: {Error}",
                    credential.CredentialId, registerId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // Credential register recording is non-fatal
            _logger.LogWarning(ex,
                "Failed to record credential {CredentialId} on register {RegisterId} — issuance still valid",
                credential.CredentialId, registerId);
        }
    }

    private async Task ValidateWalletOwnershipAsync(
        string senderWallet,
        ClaimsPrincipal? caller,
        bool allowMissingParticipant,
        CancellationToken cancellationToken)
    {
        // Skip validation for null caller (backward compat / internal calls)
        if (caller == null)
            return;

        // Skip validation for service principals (service-to-service calls)
        var tokenType = caller.FindFirst("token_type")?.Value;
        if (tokenType == "service")
        {
            _logger.LogDebug("Skipping wallet ownership validation for service principal");
            return;
        }

        var subClaim = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? caller.FindFirst("sub")?.Value;
        var orgClaim = caller.FindFirst("org_id")?.Value;

        if (string.IsNullOrEmpty(subClaim) || !Guid.TryParse(subClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Missing or invalid user identity claim");
        }

        // If org_id claim is missing, skip participant-based validation.
        // The user is authenticated via JWT; participant linkage is optional.
        if (string.IsNullOrEmpty(orgClaim) || !Guid.TryParse(orgClaim, out var orgId))
        {
            _logger.LogDebug(
                "No org_id claim present — skipping participant wallet ownership check for wallet {Wallet}",
                senderWallet);
            return;
        }

        // Look up participant for this user + org.
        // Multi-node audit CRITICAL #3 — cross-node Participant Service failures
        // (network blips between nodes) must NOT silently degrade authorization
        // to "any authenticated user". Enforcement mode is fail-closed by default;
        // dev environments opt out via Security:WalletOwnership:EnforcementMode=FailOpen.
        ParticipantInfo? participant;
        try
        {
            participant = await _participantClient.GetByUserAndOrgAsync(userId, orgId, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_walletOwnershipSettings.EnforcementMode == Configuration.WalletOwnershipEnforcementMode.FailClosed)
            {
                _logger.LogError(ex,
                    "Participant Service unavailable for wallet ownership check — rejecting request (fail-closed). Wallet: {Wallet}",
                    senderWallet);
                throw new UnauthorizedAccessException(
                    "Participant Service unavailable; cannot verify wallet ownership.");
            }

            _logger.LogWarning(ex,
                "Participant Service unavailable for wallet ownership check — allowing authenticated user (fail-open). Wallet: {Wallet}",
                senderWallet);
            return;
        }

        if (participant == null)
        {
            // Feature 103: open-participant walk-in submission. The caller has no participant
            // profile yet — they are late-bound to the action's Sender role immediately after this
            // check. Wallet ownership is enforced at signing time by the Wallet Service, so a missing
            // profile here is expected, not a failure (#911).
            if (allowMissingParticipant)
            {
                _logger.LogInformation(
                    "No participant profile for user {UserId} in org {OrgId} — allowing open-participant (Feature 103) walk-in submission. Wallet: {Wallet}",
                    userId, orgId, senderWallet);
                return;
            }

            if (!_walletOwnershipSettings.AllowMissingParticipant
                && _walletOwnershipSettings.EnforcementMode == Configuration.WalletOwnershipEnforcementMode.FailClosed)
            {
                _logger.LogWarning(
                    "No participant profile found for user {UserId} in org {OrgId} — rejecting request (fail-closed). Wallet: {Wallet}",
                    userId, orgId, senderWallet);
                throw new UnauthorizedAccessException(
                    "No participant profile linked to authenticated user.");
            }

            _logger.LogWarning(
                "No participant profile found for user {UserId} in org {OrgId} — allowing authenticated user. Wallet: {Wallet}",
                userId, orgId, senderWallet);
            return;
        }

        if (!string.Equals(participant.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Participant status is {participant.Status}");
        }

        // Verify the sender wallet is linked to this participant
        List<LinkedWalletInfo> linkedWallets;
        try
        {
            linkedWallets = await _participantClient.GetLinkedWalletsAsync(participant.Id, activeOnly: true, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_walletOwnershipSettings.EnforcementMode == Configuration.WalletOwnershipEnforcementMode.FailClosed)
            {
                _logger.LogError(ex,
                    "Failed to fetch linked wallets for participant {ParticipantId} — rejecting request (fail-closed)",
                    participant.Id);
                throw new UnauthorizedAccessException(
                    "Linked-wallets lookup failed; cannot verify wallet ownership.");
            }

            _logger.LogWarning(ex,
                "Failed to fetch linked wallets for participant {ParticipantId} — allowing authenticated user (fail-open)",
                participant.Id);
            return;
        }

        var walletMatch = linkedWallets.Any(w =>
            string.Equals(w.WalletAddress, senderWallet, StringComparison.OrdinalIgnoreCase));

        if (!walletMatch)
        {
            if (!_walletOwnershipSettings.AllowUnlinkedWallet
                && _walletOwnershipSettings.EnforcementMode == Configuration.WalletOwnershipEnforcementMode.FailClosed)
            {
                _logger.LogWarning(
                    "Wallet {Wallet} is not linked to participant {ParticipantId} — rejecting request (fail-closed)",
                    senderWallet, participant.Id);
                throw new UnauthorizedAccessException(
                    "Sender wallet is not linked to authenticated participant.");
            }

            _logger.LogWarning(
                "Wallet {Wallet} is not linked to participant {ParticipantId} — allowing authenticated user (participant system may not be fully configured)",
                senderWallet, participant.Id);
            return;
        }

        _logger.LogDebug("Wallet ownership validated: {Wallet} belongs to participant {ParticipantId}",
            senderWallet, participant.Id);
    }

    private static string GenerateIdempotencyKey(string instanceId, int actionId, string senderWallet, string? lastTransactionId = null)
    {
        var keySource = $"instance:{instanceId}:action:{actionId}:wallet:{senderWallet}:prevTx:{lastTransactionId ?? "none"}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(keySource));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Resolves recipient public keys from external keys and register (US4: Automatic register resolution with external key override).
    /// External keys take precedence over register-published keys per FR-010.
    /// Revoked participants cause a hard failure. Not-found wallets without external keys are skipped with a warning.
    /// </summary>
    private async Task<(RecipientInfo[] Recipients, string? Error)> ResolveRecipientKeysAsync(
        IEnumerable<string> walletAddresses,
        Dictionary<string, ExternalKeyInfo>? externalKeys,
        string registerId,
        CancellationToken cancellationToken)
    {
        var allWallets = walletAddresses.ToList();
        var recipients = new List<RecipientInfo>();
        var skippedRecipients = new List<string>();

        // Step 1: Resolve external keys first (they take precedence per FR-010)
        var externallyResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (externalKeys != null && externalKeys.Count > 0)
        {
            foreach (var wallet in allWallets)
            {
                if (externalKeys.TryGetValue(wallet, out var keyInfo))
                {
                    if (!Enum.TryParse<WalletNetworks>(keyInfo.Algorithm, ignoreCase: true, out var algorithm))
                    {
                        continue; // Skip unrecognized algorithms
                    }

                    recipients.Add(new RecipientInfo
                    {
                        WalletAddress = wallet,
                        PublicKey = Convert.FromBase64String(keyInfo.PublicKey),
                        Algorithm = algorithm,
                        Source = KeySource.External
                    });
                    externallyResolved.Add(wallet);
                }
            }
        }

        // Step 2: Collect wallets that still need resolution from the register
        var walletsNeedingResolution = allWallets
            .Where(w => !externallyResolved.Contains(w))
            .ToArray();

        if (walletsNeedingResolution.Length > 0)
        {
            // Step 3: Batch resolve from register
            var batchRequest = new BatchPublicKeyRequest
            {
                WalletAddresses = walletsNeedingResolution
            };

            var batchResponse = await _registerClient.ResolvePublicKeysBatchAsync(
                registerId, batchRequest, cancellationToken);

            // Step 4: Handle revoked participants — hard failure
            if (batchResponse.Revoked.Length > 0)
            {
                var revokedList = string.Join(", ", batchResponse.Revoked);
                return ([], $"Recipient {revokedList} has been revoked and cannot receive encrypted payloads");
            }

            // Step 5: Add resolved keys from register
            foreach (var (wallet, resolution) in batchResponse.Resolved)
            {
                if (!Enum.TryParse<WalletNetworks>(resolution.Algorithm, ignoreCase: true, out var algorithm))
                {
                    _logger.LogWarning(
                        "Unrecognized algorithm '{Algorithm}' for wallet {Wallet} from register — skipping",
                        resolution.Algorithm, wallet);
                    skippedRecipients.Add(wallet);
                    continue;
                }

                recipients.Add(new RecipientInfo
                {
                    WalletAddress = wallet,
                    PublicKey = Convert.FromBase64String(resolution.PublicKey),
                    Algorithm = algorithm,
                    Source = KeySource.Register
                });
            }

            // Step 6: Handle not-found wallets — skip with warning
            if (batchResponse.NotFound.Length > 0)
            {
                foreach (var wallet in batchResponse.NotFound)
                {
                    _logger.LogWarning(
                        "Public key not found on register for wallet {Wallet} — recipient skipped (no external key provided)",
                        wallet);
                    skippedRecipients.Add(wallet);
                }
            }
        }

        return (recipients.ToArray(), null);
    }

    /// <summary>
    /// Computes a deterministic group ID from sorted field names and their values.
    /// </summary>
    private static string ComputeGroupId(string[] sortedFields, Dictionary<string, object> payload)
    {
        var fieldsPart = string.Join("|", sortedFields);
        var valuesPart = System.Text.Json.JsonSerializer.Serialize(
            sortedFields.Select(f => payload.TryGetValue(f, out var v) ? v : null),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        var combined = $"{fieldsPart}\n{valuesPart}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Feature 108. Serialises the signed submission and hands it to the local Peer.Service
    /// fan-out endpoint. Errors are logged at <c>Warning</c> (so subscriber-only nodes, which
    /// depend on fan-out reaching the owner, see a clear diagnostic) and surface as a
    /// no-target-no-accepted result. The concurrent validator call is sufficient on its own
    /// when the local node owns the register or is on the roster.
    /// </summary>
    private async Task<DistributeTransactionResult> DistributeSubmissionAsync(
        string registerId,
        TransactionSubmission submission,
        CancellationToken cancellationToken)
    {
        if (_peerClient is null)
            return new DistributeTransactionResult(0, 0);

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(
                submission,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return await _peerClient.DistributeTransactionAsync(registerId, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Feature 108 — peer-service fan-out errored for register {RegisterId} transaction {TxId}; " +
                "falling back to validator-only. Subscriber nodes depend on this path to reach a carrier — " +
                "investigate if this repeats.",
                registerId, submission.TransactionId);
            return new DistributeTransactionResult(0, 0);
        }
    }

    /// <summary>
    /// Feature 111 US3 — enforce retry-gating. Throws
    /// <see cref="PresentationAlreadyCompleteException"/> when a prior
    /// PresentationOutcome with kind=success exists for this instance+action.
    /// Prior decline / abandoned outcomes do NOT block — retry is a first-class flow.
    /// </summary>
    /// <remarks>
    /// Known limitation: scans all transactions for the instance client-side.
    /// IRegisterServiceClient does not yet expose a transactionType filter; if
    /// it grows one, narrow this to PresentationOutcome server-side.
    /// </remarks>
    private async Task AssertNoPriorSuccessfulPresentationAsync(
        Sorcha.Blueprint.Service.Models.Instance instance,
        int actionId,
        CancellationToken cancellationToken)
    {
        if (actionId < 0)
        {
            // Action ids are semantically non-negative. A negative value indicates
            // a caller bug — fail loud rather than silently skipping the gate.
            throw new ArgumentOutOfRangeException(nameof(actionId), actionId,
                "Action id must be non-negative for presentation retry gating.");
        }

        try
        {
            var transactions = await _registerClient.GetTransactionsByInstanceIdAsync(
                instance.RegisterId, instance.Id, cancellationToken) ?? [];

            foreach (var tx in transactions)
            {
                if (tx.MetaData is null) continue;
                if (tx.MetaData.TransactionType != Sorcha.Register.Models.Enums.TransactionType.PresentationOutcome) continue;
                if (tx.MetaData.ActionId != (uint)actionId) continue;

                // The outcome kind lives in the transaction payload metadata;
                // on the BuiltTransaction path we set it as metadata["outcomeKind"].
                // For register-sealed transactions, TrackingData carries the same value.
                var kind = tx.MetaData.TrackingData?.GetValueOrDefault(PresentationMetadataKeys.OutcomeKind);
                if (string.Equals(kind, PresentationMetadataKeys.OutcomeKindSuccess, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Retry gate: instance {InstanceId} action {ActionId} already has a successful PresentationOutcome (tx {TxId}); rejecting new attempt",
                        instance.Id, actionId, tx.TxId);
                    throw new PresentationAlreadyCompleteException(actionId, tx.TxId);
                }
            }
        }
        catch (PresentationAlreadyCompleteException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Register query failure should not block a fresh attempt — err on the
            // side of letting the user retry. The validator's chain-integrity check
            // is the authoritative guard against double-completion.
            _logger.LogWarning(ex,
                "Retry gate: failed to query prior transactions for instance {InstanceId}; allowing attempt",
                instance.Id);
        }
    }

    /// <summary>
    /// Build the post-action <see cref="IssuedCredentialResponse"/> summary. Best-effort lookups
    /// — a participant-lookup failure degrades to a truncated DID rather than failing the response.
    /// </summary>
    private async Task<IssuedCredentialResponse> BuildIssuedCredentialResponseAsync(
        Sorcha.ServiceClients.Wallet.CredentialIssuanceResult issuedCredential,
        BlueprintModel blueprint,
        ActionModel actionDef,
        ClaimsPrincipal? caller,
        CancellationToken cancellationToken)
    {
        var issuedToName = await ResolveRecipientNameAsync(issuedCredential.SubjectDid, cancellationToken);
        var processedByName = caller?.FindFirst("name")?.Value
                              ?? caller?.FindFirst(ClaimTypes.Name)?.Value
                              ?? "Unknown";
        var processedByRole = caller?.FindFirst("role")?.Value
                              ?? caller?.FindFirst(ClaimTypes.Role)?.Value
                              ?? "Member";

        // Org name comes from the caller's JWT (org_name claim) — same source used by
        // the credential mint path. Falls back to a truncated issuer DID when missing.
        var signedByOrg = caller?.FindFirst("org_name")?.Value;
        if (string.IsNullOrEmpty(signedByOrg))
        {
            signedByOrg = TruncateDid(issuedCredential.IssuerDid);
        }

        var disclosableCount = actionDef.CredentialIssuanceConfig?.Disclosable?.Count() ?? 0;

        return new IssuedCredentialResponse
        {
            CredentialId = issuedCredential.CredentialId,
            CredentialType = string.IsNullOrEmpty(issuedCredential.Type)
                ? actionDef.CredentialIssuanceConfig?.CredentialType ?? "Credential"
                : issuedCredential.Type,
            IssuedToDid = issuedCredential.SubjectDid,
            IssuedToName = issuedToName,
            SignedByOrg = signedByOrg,
            ProcessedByName = processedByName,
            ProcessedByRole = processedByRole,
            TotalClaims = issuedCredential.Claims.Count,
            DisclosableClaims = disclosableCount,
            UsagePolicy = FormatUsagePolicy(
                actionDef.CredentialIssuanceConfig?.UsagePolicy ?? UsagePolicy.Reusable,
                actionDef.CredentialIssuanceConfig?.MaxPresentations),
            ExpiresAt = issuedCredential.ExpiresAt,
            BlueprintName = string.IsNullOrEmpty(blueprint.Title) ? blueprint.Id : blueprint.Title,
            ActionName = string.IsNullOrEmpty(actionDef.Title) ? actionDef.Id.ToString() : actionDef.Title
        };
    }

    private async Task<string> ResolveRecipientNameAsync(string subjectDid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(subjectDid))
        {
            return "Unknown recipient";
        }

        // SubjectDid is usually a wallet address (ws11q...). Try the participant lookup;
        // fall back to a truncated DID display when no participant has linked it.
        try
        {
            var participant = await _participantClient.GetByWalletAddressAsync(subjectDid, cancellationToken);
            if (participant is not null && !string.IsNullOrEmpty(participant.DisplayName))
            {
                return participant.DisplayName;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Participant lookup failed for credential subject {SubjectDid} — falling back to truncated DID",
                subjectDid);
        }

        return TruncateDid(subjectDid);
    }

    private static string TruncateDid(string did)
    {
        if (string.IsNullOrEmpty(did)) return "(unknown)";
        if (did.Length <= 16) return did;
        return $"{did[..8]}…{did[^4..]}";
    }

    private static string FormatUsagePolicy(UsagePolicy policy, int? maxPresentations) => policy switch
    {
        UsagePolicy.Reusable => "Reusable",
        UsagePolicy.SingleUse => "Single-use",
        UsagePolicy.LimitedUse => maxPresentations is > 0
            ? $"Up to {maxPresentations} presentations"
            : "Limited use",
        _ => policy.ToString()
    };
}

/// <summary>
/// Result of action data validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Result of routing evaluation
/// </summary>
public class RoutingResult
{
    public List<NextAction> NextActions { get; init; } = [];
    public bool IsParallel { get; init; }

    /// <summary>
    /// The id of the route that was taken, or null when no route matched (Feature 184). Carried onto
    /// the sender-signed routing decision so a node folding the sealed transaction can find the route
    /// — and any <c>x-decision-notice</c> on it — in the replicated blueprint, without re-evaluating
    /// conditions against a payload it may not be able to read. Populated for terminal routes too.
    /// </summary>
    public string? MatchedRouteId { get; init; }

    /// <summary>
    /// Per-next-action prepopulated payloads derived from the matched route's
    /// <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/>. Null when the
    /// matched route declares no mapping. Feature 104 wave 14a.
    /// </summary>
    public IReadOnlyDictionary<int, JsonObject>? PendingPayloads { get; init; }
}

/// <summary>
/// Exception for validation errors
/// </summary>
public class ValidationException : Exception
{
    public List<string> Errors { get; }

    public ValidationException(string message) : base(message)
    {
        Errors = [message];
    }

    public ValidationException(List<string> errors) : base(string.Join("; ", errors))
    {
        Errors = errors;
    }
}
