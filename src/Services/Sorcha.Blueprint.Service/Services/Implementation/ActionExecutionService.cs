// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Haip;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.Cryptography.Enums;
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
public class ActionExecutionService : IActionExecutionService
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
    private readonly ICredentialVerifier? _credentialVerifier;
    private readonly IStatusListManager? _statusListManager;
    private readonly IEncryptionPipelineService? _encryptionPipeline;
    private readonly IDisclosureGroupBuilder? _disclosureGroupBuilder;
    private readonly Channel<EncryptionWorkItem>? _encryptionChannel;
    private readonly IEncryptionOperationStore? _encryptionOperationStore;
    private readonly IHaipServiceClient? _haipClient;
    private readonly IPresentationLifecycleService? _presentationLifecycle;
    private readonly IPresentationRateLimiter? _presentationRateLimiter;
    private readonly IActionStore _actionStore;
    private readonly IInstanceBindingCache? _bindingCache;
    private readonly TransactionConfirmationOptions _confirmationOptions;
    private readonly bool _credentialStatusEmbeddingEnabled;
    private readonly ILogger<ActionExecutionService> _logger;
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.Service.ActionExecution");

    /// <summary>
    /// Maximum actions per workflow instance (SEC-AUDIT 3.8). Prevents routing cycles from infinite loops.
    /// </summary>
    private const int MaxExecutionDepth = 1000;

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
        IPresentationRateLimiter? presentationRateLimiter = null)
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
        _bindingCache = bindingCache;

        // Feature 093 US2: read the CredentialStatus:EnableEmbedding flag. When false,
        // ActionExecutionService skips the pre-signing status list allocation and
        // credentials are issued without the credentialStatus claim — matching pre-fix
        // behaviour for dev environments that do not run a status list manager.
        _credentialStatusEmbeddingEnabled =
            configuration?.GetValue<bool?>("CredentialStatus:EnableEmbedding") ?? true;
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
        var blueprint = await _actionResolver.GetBlueprintAsync(instance.BlueprintId, cancellationToken);
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

        // 4b. Validate wallet ownership (SEC-006)
        await ValidateWalletOwnershipAsync(request.SenderWallet, caller, cancellationToken);

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

        // 4c. Verify credential presentations against action requirements
        var haipRequirement = actionDef.CredentialRequirements?
            .FirstOrDefault(r => r.PresentationSource == PresentationSource.HaipExternalWallet);
        if (actionDef.CredentialRequirements?.Any() == true)
        {
            var hasSubmittedPresentations = request.CredentialPresentations is { Count: > 0 };

            if (haipRequirement != null && !hasSubmittedPresentations && _presentationLifecycle != null)
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
                            "Presentation attempt rate-limited for wallet={Wallet} register={RegisterId} count={Count}/{Threshold}",
                            request.SenderWallet, instance.RegisterId, rateCheck.CurrentCount, rateCheck.Threshold);
                        throw new PresentationRateLimitedException(rateCheck.RetryAfter);
                    }
                }

                var lifecycleResult = await _presentationLifecycle.InitiateAsync(
                    blueprint, instance, actionDef, haipRequirement,
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
                    PresentationRequest = new HaipPresentationRequestResponse
                    {
                        RequestId = lifecycleResult.PresentationRequestId,
                        PresentationRequestUri = lifecycleResult.AuthorizationRequestUri,
                        CredentialType = haipRequirement.Type,
                        RequestedClaims = haipRequirement.RequiredClaims?
                            .Select(c => c.ClaimName).ToList(),
                        ExpiresAt = lifecycleResult.ExpiresAt
                    }
                };
            }
            else if (haipRequirement != null && !hasSubmittedPresentations)
            {
                // Feature 111 removed the legacy single-shot HAIP path. If we reach
                // this branch it means the blueprint requires a HAIP presentation but
                // the lifecycle service is not registered — a deployment configuration
                // error rather than a runtime scenario the code should silently handle.
                throw new InvalidOperationException(
                    "HAIP presentation requested but IPresentationLifecycleService is not registered. " +
                    "Ensure PresentationLifecycleOptions and related services are wired in the DI container.");
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

        // 5c. For starting actions with no prior transactions, chain from the blueprint
        // publish TX. Each instance forks from the blueprint publish TX by design — the
        // validator allows multiple children of Control transactions.
        if (string.IsNullOrEmpty(accumulatedState.PreviousTransactionId) && actionDef.IsStartingAction)
        {
            var blueprintTxId = ComputeBlueprintPublishTxId(instance.RegisterId, instance.BlueprintId);
            await WaitForTransactionConfirmationAsync(instance.RegisterId, blueprintTxId, cancellationToken);

            _logger.LogInformation(
                "Action 0 for instance {InstanceId}: PrevTxId set to blueprint publish TX {BlueprintTxId}",
                instanceId, blueprintTxId);

            accumulatedState = accumulatedState with { PreviousTransactionId = blueprintTxId };
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

        // 8b. HAIP credential mint (Feature 097 + Feature 104 wave 14b).
        //     Moved to run BEFORE routing so the minted offer data can be
        //     carried forward to the claim action via Route.OutputMapping.
        //     Internal Sorcha issuance (issuedCredential) still runs later at
        //     step 9d because it depends on the built transaction context.
        CreateOfferResult? haipOfferResult = null;
        if (actionDef.CredentialIssuanceConfig != null
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
                    mergedData!);
                var haipClaimsForWire = haipClaims.ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

                _logger.LogInformation(
                    "Routing credential issuance to HAIP service for external wallet: type={Type}, claims=[{ClaimNames}]",
                    actionDef.CredentialIssuanceConfig.CredentialType,
                    string.Join(", ", haipClaimsForWire.Keys));

                haipOfferResult = await _haipClient.CreateCredentialOfferAsync(
                    request.SenderWallet,
                    instance.RegisterId,
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
        var issuerOrgName = caller?.FindFirst("org_name")?.Value;
        var issuerTenantId = caller?.FindFirst("org_id")?.Value;
        if (actionDef.CredentialIssuanceConfig != null
            && actionDef.CredentialIssuanceConfig.TargetAudience is TargetAudience.SorchaLocalWallet
                or TargetAudience.SorchaInternal)
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

            try
            {
                localWalletCredential = await IssueCredentialFromActionAsync(
                    actionDef, mergedData, request.SenderWallet, instance, issuerOrgName, issuerTenantId, cancellationToken);
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
            // US4: Automatic register resolution with external key override
            var (recipients, resolveError) = await ResolveRecipientKeysAsync(
                disclosedPayloads.Keys, request.ExternalRecipientKeys, instance.RegisterId, cancellationToken);
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

        var validatorTask = _validatorClient.SubmitTransactionAsync(submission, cancellationToken);
        var distributeTask = _peerClient is null
            ? Task.FromResult(new DistributeTransactionResult(0, 0, LocallyOwned: true))
            : DistributeSubmissionAsync(instance.RegisterId, submission, cancellationToken);

        await Task.WhenAll(validatorTask, distributeTask);
        var validatorResult = validatorTask.Result;
        var distributeResult = distributeTask.Result;

        if (!validatorResult.Success && distributeResult.AcceptedCount == 0 && !distributeResult.LocallyOwned)
        {
            throw new InvalidOperationException(
                $"Validator rejected transaction {transaction.TxId}: [{validatorResult.ErrorCode}] {validatorResult.ErrorMessage} — and no peer accepted the fan-out");
        }

        // Surface local-validator rejections even when a peer accepted. The concurrent peer
        // path doesn't mask a structural rejection (schema violation, sequence-number mismatch,
        // double-spend) — operators need to see these so they can intervene. We don't throw
        // here because the accepting peer (typically the register owner) will also re-run
        // validation under the same rules; if they accept, the tx is genuinely admissible.
        if (!validatorResult.Success)
        {
            _logger.LogWarning(
                "Local validator rejected transaction {TxId} for register {RegisterId}: [{ErrorCode}] {ErrorMessage}. " +
                "Continuing because peer fan-out accepted on {AcceptedCount}/{TargetCount} peer(s)" +
                "{LocallyOwnedNote}",
                transaction.TxId, instance.RegisterId,
                validatorResult.ErrorCode, validatorResult.ErrorMessage,
                distributeResult.AcceptedCount, distributeResult.TargetPeerCount,
                distributeResult.LocallyOwned ? " (locally-owned register, no fan-out attempted)" : string.Empty);
        }

        _logger.LogDebug(
            "Transaction {TxId} submitted: validator={ValidatorSuccess}, peers accepted={PeersAccepted}/{PeersAttempted}, locallyOwned={LocallyOwned}",
            transaction.TxId, validatorResult.Success,
            distributeResult.AcceptedCount, distributeResult.TargetPeerCount, distributeResult.LocallyOwned);

        _logger.LogInformation(
            "Transaction {TxId} submitted to Validator for register {RegisterId}. Waiting for docket confirmation...",
            transaction.TxId, instance.RegisterId);

        // 13. Poll Register Service until transaction appears with a DocketNumber (confirmation)
        var confirmedTxId = transaction.TxId;
        await WaitForTransactionConfirmationAsync(instance.RegisterId, confirmedTxId, cancellationToken);

        // 13b. Store idempotency key (24-hour TTL)
        await _actionStore.StoreIdempotencyKeyAsync(idempotencyKey, confirmedTxId, TimeSpan.FromHours(24));

        // 14. Persist accumulated data on instance for subsequent actions' routing/calculations
        //     (fallback when Register-based state reconstruction is unavailable)
        //     Only store fields from the action's schema or explicit calculations (SEC-AUDIT 3.6)
        var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.PayloadData != null)
        {
            foreach (var key in request.PayloadData.Keys)
                allowedFields.Add(key);
        }
        if (actionDef.Calculations != null)
        {
            foreach (var calc in actionDef.Calculations)
            {
                allowedFields.Add(calc.Key);
            }
        }

        foreach (var kvp in mergedData.Where(kvp => allowedFields.Contains(kvp.Key)))
        {
            instance.AccumulatedData[kvp.Key] = kvp.Value;
        }

        // 14a. Generate instance reference on first action (idempotent)
        if (!instance.Metadata.ContainsKey("instanceReference"))
        {
            var instanceRef = Sorcha.Blueprint.Engine.Implementation.InstanceReferenceGenerator.Generate(
                blueprint.InstanceReference,
                instance.AccumulatedData,
                instance.Id,
                blueprint.Title);
            instance.Metadata["instanceReference"] = instanceRef;
            _logger.LogInformation(
                "Generated instance reference {Reference} for instance {InstanceId}",
                instanceRef, instance.Id);
        }

        // 14b. Update instance state
        instance = await UpdateInstanceAfterExecutionAsync(
            instance,
            actionId,
            confirmedTxId,
            routingResult,
            cancellationToken);

        // 15. Notify participants via SignalR
        await NotifyParticipantsAsync(instance, actionDef, routingResult, cancellationToken);

        // 15a. Action confirmed — participants already notified via thin signals in step 15

        // 15b. Update issued credential with confirmed transaction ID
        if (issuedCredential != null)
        {
            _logger.LogInformation(
                "Credential {CredentialId} of type {Type} issued from {Issuer} to {Recipient} (tx: {TxId})",
                issuedCredential.CredentialId, issuedCredential.Type,
                issuedCredential.IssuerDid, issuedCredential.SubjectDid, confirmedTxId);

            // 15c. Record credential on dedicated register if configured (FR-014c)
            if (!string.IsNullOrEmpty(actionDef.CredentialIssuanceConfig?.RegisterId))
            {
                await RecordCredentialOnRegisterAsync(
                    issuedCredential,
                    actionDef.CredentialIssuanceConfig.RegisterId,
                    request.SenderWallet,
                    instanceId,
                    confirmedTxId,
                    cancellationToken);
            }
        }

        // 16. Build response
        var response = new ActionSubmissionResponse
        {
            TransactionId = confirmedTxId,
            InstanceId = instanceId,
            NextActions = routingResult.NextActions.Select(na => new NextActionResponse
            {
                ActionId = na.ActionId,
                ActionTitle = na.ActionTitle,
                ParticipantId = na.ParticipantId,
                BranchId = na.BranchId
            }).ToList(),
            Calculations = calculations,
            IsComplete = routingResult.NextActions.Count == 0,
            Warnings = validationResult.Warnings,
            IssuedCredentialId = issuedCredential?.CredentialId,
            CredentialOffer = haipOfferResult != null
                ? new HaipCredentialOfferResponse
                {
                    OfferId = haipOfferResult.OfferId,
                    CredentialOfferUri = haipOfferResult.CredentialOfferUri,
                    CredentialType = actionDef.CredentialIssuanceConfig?.CredentialType ?? string.Empty,
                    ExpiresAt = haipOfferResult.ExpiresAt
                }
                : null,
            // Feature 111 — presentation requests now return via the 202 Accepted
            // short-circuit earlier in ExecuteAsync. The 200 OK response never
            // carries a PresentationRequest; this field is preserved for
            // backwards-compatible schema shape only.
            PresentationRequest = null
        };

        _logger.LogInformation(
            "Action {ActionId} executed successfully for instance {InstanceId}. Transaction: {TxId}, Complete: {IsComplete}",
            actionId, instanceId, confirmedTxId, response.IsComplete);

        return response;
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
        var blueprint = await _actionResolver.GetBlueprintAsync(instance.BlueprintId, cancellationToken);
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

        // 6b. Validate wallet ownership (SEC-006)
        var rejectWallet = request.SenderWallet ?? instance.ParticipantWallets.Values.FirstOrDefault() ?? "";
        await ValidateWalletOwnershipAsync(rejectWallet, caller, cancellationToken);

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
        instance = await _instanceStore.UpdateAsync(instance, cancellationToken);

        // 10. Notify target participant via thin signal
        var targetParticipantId = actionDef.RejectionConfig.TargetParticipantId ?? targetAction.Sender;
        string? targetWalletAddress = null;
        instance.ParticipantWallets?.TryGetValue(targetParticipantId, out targetWalletAddress);
        await _notificationService.NotifyActionRejectedAsync(
            instanceId,
            targetWalletAddress,
            cancellationToken);

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

    private async Task<Dictionary<string, Dictionary<string, object>>> ApplyDisclosuresAsync(
        ActionModel action,
        Dictionary<string, object> data,
        BlueprintModel blueprint,
        Dictionary<string, string> participantWallets,
        string registerId)
    {
        // Delegate to the Blueprint Engine for JSON Pointer disclosure filtering
        var engineResults = _executionEngine.ApplyDisclosures(data, action);

        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>();

        foreach (var result in engineResults)
        {
            // Resolve participant ID to wallet address (2-tier: instance bindings → register)
            var recipientAddress = result.ParticipantId;
            if (participantWallets.TryGetValue(recipientAddress, out var walletAddress))
            {
                recipientAddress = walletAddress;
            }
            else
            {
                // Tier 2: Try resolving from register participant index
                var participant = blueprint.Participants.FirstOrDefault(p =>
                    string.Equals(p.Id, recipientAddress, StringComparison.OrdinalIgnoreCase));

                if (participant != null)
                {
                    try
                    {
                        var resolvedRecord = await _registerClient.ResolveParticipantAsync(
                            registerId, participant.Id, participant.Organisation);

                        if (resolvedRecord?.Addresses.Count > 0)
                        {
                            var primaryAddr = resolvedRecord.Addresses.FirstOrDefault(a => a.Primary)
                                              ?? resolvedRecord.Addresses.First();
                            recipientAddress = primaryAddr.WalletAddress;
                            _logger.LogDebug(
                                "Resolved disclosure recipient {ParticipantId} to wallet {Wallet} from register",
                                result.ParticipantId, recipientAddress);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve participant {ParticipantId} from register",
                            result.ParticipantId);
                    }
                }
            }

            if (string.IsNullOrEmpty(recipientAddress))
            {
                _logger.LogWarning("No wallet address for disclosure recipient {ParticipantId}", result.ParticipantId);
                continue;
            }

            disclosedPayloads[recipientAddress] = result.DisclosedData;
        }

        return disclosedPayloads;
    }

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
                return await _instanceStore.UpdateAsync(instance, cancellationToken);
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
                cancellationToken);
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
        IReadOnlyDictionary<string, object?> mergedData)
        => BuildClaimsFromMappings(mappings, mergedData, _logger);

    /// <summary>
    /// Static logger-injected overload used by unit tests so the helper can
    /// be exercised without needing the full <see cref="ActionExecutionService"/>
    /// constructor graph.
    /// </summary>
    internal static Dictionary<string, object?> BuildClaimsFromMappings(
        IEnumerable<Sorcha.Blueprint.Models.Credentials.ClaimMapping>? mappings,
        IReadOnlyDictionary<string, object?> mergedData,
        ILogger logger)
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
                        PortraitOversizeWarningCode);
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
    /// Local duplicate of <c>ValidationErrorCodes.CredentialPortraitOversize</c>
    /// (defined in <c>Sorcha.Validator.Service</c>). Blueprint.Service does not
    /// reference Validator.Service, so the code is stringified here with this
    /// explicit comment binding the two — if either side is renamed, fix both.
    /// </summary>
    private const string PortraitOversizeWarningCode = "WARN_CRED_PORTRAIT_OVERSIZE_001";

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
        var claims = BuildClaimsFromMappings(config.ClaimMappings, mergedData!);
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

        if (_statusListManager != null && _credentialStatusEmbeddingEnabled)
        {
            try
            {
                var preAllocationId = $"pending-{Guid.NewGuid()}";
                var allocation = await _statusListManager.AllocateIndexAsync(
                    senderWallet, instance.RegisterId, preAllocationId, cancellationToken);
                preAllocatedStatusListUrl = allocation.StatusListUrl;
                preAllocatedStatusListIndex = allocation.Index;

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
                skipRecipientStore: config.TargetAudience is TargetAudience.SorchaLocalWallet or TargetAudience.SorchaInternal,
                issuerOrgName: issuerOrgName,
                tenantId: issuerTenantId,
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
        // If the Participant Service is unavailable or the user has no profile,
        // degrade gracefully — the user is already authenticated via JWT.
        ParticipantInfo? participant;
        try
        {
            participant = await _participantClient.GetByUserAndOrgAsync(userId, orgId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Participant Service unavailable for wallet ownership check — allowing authenticated user. Wallet: {Wallet}",
                senderWallet);
            return;
        }

        if (participant == null)
        {
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
            _logger.LogWarning(ex,
                "Failed to fetch linked wallets for participant {ParticipantId} — allowing authenticated user",
                participant.Id);
            return;
        }

        var walletMatch = linkedWallets.Any(w =>
            string.Equals(w.WalletAddress, senderWallet, StringComparison.OrdinalIgnoreCase));

        if (!walletMatch)
        {
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
    /// Computes the deterministic TX ID for a blueprint publish transaction.
    /// This is the same formula used by the Register Service when publishing blueprints:
    /// SHA-256("blueprint-publish-{registerId}-{blueprintId}") as lowercase hex.
    /// </summary>
    public static string ComputeBlueprintPublishTxId(string registerId, string blueprintId)
    {
        var txIdSource = Encoding.UTF8.GetBytes($"blueprint-publish-{registerId}-{blueprintId}");
        var hash = SHA256.HashData(txIdSource);
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
            return new DistributeTransactionResult(0, 0, LocallyOwned: true);

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
                "falling back to validator-only. Subscriber nodes depend on this path to reach the owner — " +
                "investigate if this repeats.",
                registerId, submission.TransactionId);
            return new DistributeTransactionResult(0, 0, LocallyOwned: false);
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
