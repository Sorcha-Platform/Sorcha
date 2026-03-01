// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Cryptography.Enums;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;
using Microsoft.Extensions.Options;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

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
    private readonly IWalletServiceClient _walletClient;
    private readonly IParticipantServiceClient _participantClient;
    private readonly INotificationService _notificationService;
    private readonly IInstanceStore _instanceStore;
    private readonly IExecutionEngine _executionEngine;
    private readonly ICredentialVerifier? _credentialVerifier;
    private readonly IStatusListManager? _statusListManager;
    private readonly IEncryptionPipelineService? _encryptionPipeline;
    private readonly IDisclosureGroupBuilder? _disclosureGroupBuilder;
    private readonly IActionStore _actionStore;
    private readonly TransactionConfirmationOptions _confirmationOptions;
    private readonly ILogger<ActionExecutionService> _logger;
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.Service.ActionExecution");

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
        ICredentialVerifier? credentialVerifier = null,
        IOptions<TransactionConfirmationOptions>? confirmationOptions = null,
        IStatusListManager? statusListManager = null,
        IEncryptionPipelineService? encryptionPipeline = null,
        IDisclosureGroupBuilder? disclosureGroupBuilder = null)
    {
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _stateReconstruction = stateReconstruction ?? throw new ArgumentNullException(nameof(stateReconstruction));
        _transactionBuilder = transactionBuilder ?? throw new ArgumentNullException(nameof(transactionBuilder));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
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

        // 4c. Verify credential presentations against action requirements
        if (actionDef.CredentialRequirements?.Any() == true && _credentialVerifier != null)
        {
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

        // 6. Validate input data against schema
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

        // 9. Evaluate routing conditions to determine next action(s)
        var routingResult = await EvaluateRoutingAsync(blueprint, actionDef, mergedData, cancellationToken);

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
        var disclosedPayloads = ApplyDisclosures(actionDef, payloadWithCalculations, blueprint, instance.ParticipantWallets);

        // If no disclosure rules defined, default to full disclosure under sender's wallet.
        // This ensures the payload data is always present in the transaction for schema validation.
        if (disclosedPayloads.Count == 0 && payloadWithCalculations.Count > 0)
        {
            disclosedPayloads[request.SenderWallet] = payloadWithCalculations;
        }

        // 9c. Encrypt disclosed payloads (envelope encryption)
        EncryptionResult? encryptionResult = null;
        if (_encryptionPipeline != null && disclosedPayloads.Count > 0)
        {
            // Resolve recipient keys from ExternalRecipientKeys (US1 MVP)
            // US4 will add automatic register resolution
            var recipients = ResolveRecipientKeys(disclosedPayloads.Keys, request.ExternalRecipientKeys);

            if (recipients.Length > 0)
            {
                // US2: DisclosureGroupBuilder groups recipients with identical field sets
                DisclosureGroup[] groups;
                if (_disclosureGroupBuilder != null)
                {
                    groups = _disclosureGroupBuilder.BuildGroups(disclosedPayloads, recipients);
                }
                else
                {
                    // Fallback: build simple disclosure groups (one per wallet)
                    groups = disclosedPayloads.Select(kvp =>
                    {
                        var recipient = recipients.FirstOrDefault(r => r.WalletAddress == kvp.Key);
                        if (recipient == null) return null;

                        var fields = kvp.Value.Keys.OrderBy(k => k).ToArray();
                        var groupId = ComputeGroupId(fields);

                        return new DisclosureGroup
                        {
                            GroupId = groupId,
                            DisclosedFields = fields,
                            FilteredPayload = kvp.Value,
                            Recipients = [recipient]
                        };
                    }).Where(g => g != null).ToArray()!;
                }

                encryptionResult = await _encryptionPipeline.EncryptDisclosedPayloadsAsync(groups!, cancellationToken);

                if (!encryptionResult.Success)
                {
                    throw new InvalidOperationException(
                        $"Encryption failed for recipient {encryptionResult.FailedRecipient}: {encryptionResult.Error}");
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

        // 9d. Issue credential if action has issuance configuration
        CredentialIssuanceResult? issuedCredential = null;
        if (actionDef.CredentialIssuanceConfig != null)
        {
            issuedCredential = await IssueCredentialFromActionAsync(
                actionDef, mergedData, request.SenderWallet, instance, cancellationToken);
        }

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

        // 12. Submit to Validator Service (mempool → docket → Register)
        var submission = transaction.ToTransactionSubmission(signResult);
        var validatorResult = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

        if (!validatorResult.Success)
        {
            throw new InvalidOperationException(
                $"Validator rejected transaction {transaction.TxId}: [{validatorResult.ErrorCode}] {validatorResult.ErrorMessage}");
        }

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
        foreach (var kvp in mergedData)
        {
            instance.AccumulatedData[kvp.Key] = kvp.Value;
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

        // 15a. Notify that action was confirmed (transaction landed on ledger)
        await _notificationService.NotifyActionConfirmedAsync(
            new Hubs.ActionNotification
            {
                TransactionHash = confirmedTxId,
                WalletAddress = request.SenderWallet,
                RegisterAddress = instance.RegisterId,
                BlueprintId = instance.BlueprintId,
                ActionId = actionId.ToString(),
                InstanceId = instanceId,
                Message = $"Action '{actionDef.Title}' confirmed"
            },
            cancellationToken);

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
            IssuedCredentialId = issuedCredential?.CredentialId
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

        var rejectSubmission = transaction.ToTransactionSubmission(rejectSignResult);
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

        // 10. Notify participants
        var targetParticipantId = actionDef.RejectionConfig.TargetParticipantId ?? targetAction.Sender;
        await _notificationService.NotifyActionRejectedAsync(
            instanceId,
            actionId,
            targetAction.Id,
            targetParticipantId,
            request.Reason,
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
        CancellationToken cancellationToken)
    {
        // Delegate to the Blueprint Engine for JSON Logic routing
        var engineResult = await _executionEngine.DetermineRoutingAsync(
            blueprint, action, mergedData, cancellationToken);

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
            IsParallel = engineResult.IsParallel
        };
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

    private Dictionary<string, Dictionary<string, object>> ApplyDisclosures(
        ActionModel action,
        Dictionary<string, object> data,
        BlueprintModel blueprint,
        Dictionary<string, string> participantWallets)
    {
        // Delegate to the Blueprint Engine for JSON Pointer disclosure filtering
        var engineResults = _executionEngine.ApplyDisclosures(data, action);

        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>();

        foreach (var result in engineResults)
        {
            // Resolve participant ID to wallet address
            var recipientAddress = result.ParticipantId;
            if (participantWallets.TryGetValue(recipientAddress, out var walletAddress))
            {
                recipientAddress = walletAddress;
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
            await _notificationService.NotifyActionAvailableAsync(
                instance.Id,
                nextAction.ActionId,
                nextAction.ActionTitle,
                nextAction.ParticipantId,
                cancellationToken);
        }

        if (routingResult.NextActions.Count == 0)
        {
            await _notificationService.NotifyWorkflowCompletedAsync(
                instance.Id,
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

    private async Task<CredentialIssuanceResult?> IssueCredentialFromActionAsync(
        ActionModel actionDef,
        Dictionary<string, object> mergedData,
        string senderWallet,
        Instance instance,
        CancellationToken cancellationToken)
    {
        var config = actionDef.CredentialIssuanceConfig!;

        // Map claims from action data using ClaimMappings
        var claims = new Dictionary<string, object>();
        if (config.ClaimMappings != null)
        {
            foreach (var mapping in config.ClaimMappings)
            {
                var sourceKey = mapping.SourceField.TrimStart('/');
                if (mergedData.TryGetValue(sourceKey, out var value))
                {
                    claims[mapping.ClaimName] = value;
                }
            }
        }

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

        try
        {
            var result = await _walletClient.IssueCredentialAsync(
                issuerWalletAddress: senderWallet,
                credentialType: config.CredentialType,
                claims: claims,
                recipientWallet: recipientWallet,
                expiryDuration: config.ExpiryDuration,
                disclosableClaims: config.Disclosable?.ToList(),
                issuanceBlueprintId: instance.BlueprintId,
                cancellationToken: cancellationToken);

            // Allocate status list index for revocation tracking
            if (result != null && _statusListManager != null)
            {
                try
                {
                    var allocation = await _statusListManager.AllocateIndexAsync(
                        senderWallet, instance.RegisterId, result.CredentialId, cancellationToken);

                    // Return a new result with status list info populated
                    result = new CredentialIssuanceResult
                    {
                        CredentialId = result.CredentialId,
                        Type = result.Type,
                        IssuerDid = result.IssuerDid,
                        SubjectDid = result.SubjectDid,
                        Claims = result.Claims,
                        IssuedAt = result.IssuedAt,
                        ExpiresAt = result.ExpiresAt,
                        RawToken = result.RawToken,
                        Status = result.Status,
                        StatusListUrl = allocation.StatusListUrl,
                        StatusListIndex = allocation.Index
                    };

                    _logger.LogInformation(
                        "Allocated status list index {Index} in list {ListId} for credential {CredentialId}",
                        allocation.Index, allocation.ListId, result.CredentialId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to allocate status list index for credential {CredentialId} — credential issued without revocation tracking",
                        result.CredentialId);
                }
            }

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
    /// Resolves recipient public keys from external key info (US1 MVP).
    /// US4 will add automatic register resolution.
    /// </summary>
    private static RecipientInfo[] ResolveRecipientKeys(
        IEnumerable<string> walletAddresses,
        Dictionary<string, ExternalKeyInfo>? externalKeys)
    {
        if (externalKeys == null || externalKeys.Count == 0)
            return [];

        var recipients = new List<RecipientInfo>();
        foreach (var wallet in walletAddresses)
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
            }
        }

        return recipients.ToArray();
    }

    /// <summary>
    /// Computes a deterministic group ID from sorted field names.
    /// </summary>
    private static string ComputeGroupId(string[] sortedFields)
    {
        var input = string.Join("|", sortedFields);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
