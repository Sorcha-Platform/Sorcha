// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using System.Text.Json;
using Sorcha.ServiceClients.Events;
using Sorcha.ServiceClients.Events.Models;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.TransactionHandler.Encryption;
using EncryptionOpStatus = Sorcha.Blueprint.Service.Models.EncryptionOperationStatus;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Background service that processes encryption work items from a channel.
/// Encrypts payloads, builds transactions, signs, and submits to the validator.
/// Sends real-time SignalR notifications at each step.
/// </summary>
public sealed class EncryptionBackgroundService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Sorcha.Encryption.BackgroundService");

    private readonly Channel<EncryptionWorkItem> _channel;
    private readonly IEncryptionOperationStore _operationStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EncryptionBackgroundService> _logger;

    // Step constants for progress tracking
    internal const int StepResolvingKeys = 1;
    internal const int StepEncrypting = 2;
    internal const int StepBuildingTransaction = 3;
    internal const int StepSubmitting = 4;
    internal const int TotalSteps = 4;

    /// <summary>Initialises a new instance of the <see cref="EncryptionBackgroundService"/> class.</summary>
    public EncryptionBackgroundService(
        Channel<EncryptionWorkItem> channel,
        IEncryptionOperationStore operationStore,
        IServiceScopeFactory scopeFactory,
        ILogger<EncryptionBackgroundService> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EncryptionBackgroundService started");

        await foreach (var workItem in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessWorkItemAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("EncryptionBackgroundService stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Never let the background service crash
                _logger.LogError(ex, "Unhandled error processing encryption work item {OperationId}", workItem.OperationId);
            }
        }

        _logger.LogInformation("EncryptionBackgroundService stopped");
    }

    /// <summary>
    /// Process a single encryption work item through the full pipeline.
    /// </summary>
    internal async Task ProcessWorkItemAsync(EncryptionWorkItem workItem, CancellationToken ct)
    {
        var operationId = workItem.OperationId;
        using var activity = ActivitySource.StartActivity("ProcessEncryptionWorkItem");
        activity?.SetTag("encryption.operation_id", operationId);
        activity?.SetTag("encryption.instance_id", workItem.InstanceId);
        activity?.SetTag("encryption.sender_wallet", workItem.SenderWallet);

        _logger.LogInformation("Processing encryption work item {OperationId} for instance {InstanceId}",
            operationId, workItem.InstanceId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var encryptionPipeline = scope.ServiceProvider.GetRequiredService<IEncryptionPipelineService>();
        var transactionBuilder = scope.ServiceProvider.GetRequiredService<ITransactionBuilderService>();
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();
        var validatorClient = scope.ServiceProvider.GetRequiredService<IValidatorServiceClient>();
        // Feature 108 — optional peer fan-out (subscriber nodes forward to the register owner).
        var peerClient = scope.ServiceProvider.GetService<IPeerServiceClient>();

        try
        {
            // Step 1: Resolving Keys
            await UpdateOperationStepAsync(operationId, EncryptionOpStatus.ResolvingKeys,
                StepResolvingKeys, "Resolving recipient keys", 10);
            await notificationService.NotifyEncryptionProgressAsync(workItem.SenderWallet,
                new EncryptionSignal { OperationId = operationId, PercentComplete = 10, Status = EncryptionStatuses.Encrypting }, ct);

            // Pre-flight size check (T051) — fail fast before expensive encryption
            var sizeCheck = encryptionPipeline.CheckSizeLimit(workItem.DisclosureGroups);
            if (!sizeCheck.WithinLimit)
            {
                await HandleFailureAsync(operationId, workItem.SenderWallet,
                    $"Estimated encrypted size ({sizeCheck.EstimatedBytes} bytes) exceeds limit ({sizeCheck.LimitBytes} bytes)",
                    null, StepResolvingKeys,
                    notificationService, scope.ServiceProvider, workItem, ct);
                return;
            }

            // Step 2: Encrypting
            await UpdateOperationStepAsync(operationId, EncryptionOpStatus.Encrypting,
                StepEncrypting, "Encrypting payloads", 30);
            await notificationService.NotifyEncryptionProgressAsync(workItem.SenderWallet,
                new EncryptionSignal { OperationId = operationId, PercentComplete = 30, Status = EncryptionStatuses.Encrypting }, ct);

            var encryptionResult = await encryptionPipeline.EncryptDisclosedPayloadsAsync(
                workItem.DisclosureGroups, ct);

            // Per-recipient detail stored in operation store for pull-back (no per-recipient signals)
            var totalRecipients = encryptionResult.RecipientProgressEntries.Length;

            // Update operation store with per-recipient status
            var recipientStatuses = encryptionResult.RecipientProgressEntries
                .Select(rp => new RecipientOperationStatus
                {
                    Name = rp.DisplayName ?? rp.WalletAddress,
                    DisclosedFields = rp.DisclosedFields,
                    Status = rp.Status.ToString().ToLowerInvariant()
                })
                .ToList();

            var opForRecipients = await _operationStore.GetByIdAsync(operationId);
            if (opForRecipients != null)
            {
                opForRecipients.Recipients = recipientStatuses;
                await _operationStore.UpdateAsync(opForRecipients);
            }

            if (!encryptionResult.Success)
            {
                await HandleFailureAsync(operationId, workItem.SenderWallet,
                    $"Encryption failed: {encryptionResult.Error}",
                    encryptionResult.FailedRecipient, StepEncrypting,
                    notificationService, scope.ServiceProvider, workItem, ct);
                return;
            }

            // Step 3: Building Transaction
            await UpdateOperationStepAsync(operationId, EncryptionOpStatus.BuildingTransaction,
                StepBuildingTransaction, "Building transaction", 60);
            await notificationService.NotifyEncryptionProgressAsync(workItem.SenderWallet,
                new EncryptionSignal { OperationId = operationId, PercentComplete = 60, Status = EncryptionStatuses.Encrypting }, ct);

            // Resolve blueprint and instance for transaction building
            var actionResolver = scope.ServiceProvider.GetRequiredService<IActionResolverService>();
            var instanceStore = scope.ServiceProvider.GetRequiredService<Sorcha.Blueprint.Service.Storage.IInstanceStore>();
            var instance = await instanceStore.GetAsync(workItem.InstanceId)
                ?? throw new InvalidOperationException($"Instance {workItem.InstanceId} not found");
            var blueprint = await actionResolver.GetBlueprintAsync(instance.BlueprintId, ct)
                ?? throw new InvalidOperationException($"Blueprint {instance.BlueprintId} not found");
            var actionDef = actionResolver.GetActionDefinition(blueprint, workItem.ActionId.ToString())
                ?? throw new InvalidOperationException($"Action {workItem.ActionId} not found in blueprint {instance.BlueprintId}");

            var transaction = await transactionBuilder.BuildEncryptedActionTransactionAsync(
                blueprint, instance, actionDef,
                workItem.PayloadWithCalculations,
                encryptionResult.Groups,
                workItem.PreviousTransactionId,
                ct);

            // Feature 137 (C5): carry the resolved next action so a cross-node mirror can seed
            // CurrentActionIds when this tx seals. Mirrors the synchronous path in
            // ActionExecutionService; DocketBuildTriggerService projects it onto TransactionMetaData.
            var nextActionId = workItem.RoutingResult.NextActions.FirstOrDefault()?.ActionId;
            if (nextActionId.HasValue)
            {
                transaction.Metadata["nextActionId"] = nextActionId.Value.ToString();
            }

            // Step 4: Signing and Submitting
            await UpdateOperationStepAsync(operationId, EncryptionOpStatus.Submitting,
                StepSubmitting, "Signing and submitting", 80);
            await notificationService.NotifyEncryptionProgressAsync(workItem.SenderWallet,
                new EncryptionSignal { OperationId = operationId, PercentComplete = 80, Status = EncryptionStatuses.Encrypting }, ct);

            var signResult = await walletClient.SignTransactionAsync(
                workItem.SenderWallet,
                transaction.SigningData,
                derivationPath: null,
                isPreHashed: false,
                ct);

            transaction.SenderWallet = workItem.SenderWallet;
            transaction.Signature = signResult.Signature;

            var nextSeqNum = await validatorClient.GetNextSequenceNumberAsync(
                workItem.RegisterId, workItem.SenderWallet, ct);
            var submission = transaction.ToTransactionSubmission(signResult, nextSeqNum);

            // Feature 108 / Feature 137 — submit to BOTH the local validator (seals iff this node is
            // on the roster) AND the peer-service fan-out (forwards to source peers for subscribed
            // registers). This async/encrypted path previously skipped the fan-out, so a replica-
            // origin encrypted submission never reached the register owner and never sealed.
            var validatorTask = validatorClient.SubmitTransactionAsync(submission, ct);
            var distributeTask = peerClient is null
                ? Task.FromResult(new DistributeTransactionResult(0, 0, LocallyOwned: true))
                : DistributeEncryptedSubmissionAsync(peerClient, workItem.RegisterId, submission, ct);
            await Task.WhenAll(validatorTask, distributeTask);
            var validatorResult = validatorTask.Result;
            var distributeResult = distributeTask.Result;

            _logger.LogInformation(
                "Encrypted submission {TxId} for register {RegisterId}: validator success={ValidatorSuccess}, fan-out targets={Targets} accepted={Accepted} locallyOwned={Local}",
                transaction.TxId, workItem.RegisterId, validatorResult.Success,
                distributeResult.TargetPeerCount, distributeResult.AcceptedCount, distributeResult.LocallyOwned);

            // Fail only when the local validator rejected AND no peer accepted the fan-out AND the
            // register is not locally owned — a subscriber's local validator never seals (off-roster),
            // so a peer-accepted submission is the success signal there.
            if (!validatorResult.Success && distributeResult.AcceptedCount == 0 && !distributeResult.LocallyOwned)
            {
                await HandleFailureAsync(operationId, workItem.SenderWallet,
                    $"Validator rejected transaction: [{validatorResult.ErrorCode}] {validatorResult.ErrorMessage} — and no peer accepted the fan-out",
                    null, StepSubmitting,
                    notificationService, scope.ServiceProvider, workItem, ct);
                return;
            }

            // Wait for transaction confirmation on the ledger, then advance the instance
            var txHash = transaction.TxId;
            await WaitForConfirmationAndAdvanceInstanceAsync(scope, workItem, txHash, ct);

            // Complete
            var op = await _operationStore.GetByIdAsync(operationId);
            if (op != null)
            {
                op.Status = EncryptionOpStatus.Complete;
                op.TransactionHash = txHash;
                op.PercentComplete = 100;
                op.CompletedAt = DateTimeOffset.UtcNow;
                await _operationStore.UpdateAsync(op);
            }

            await notificationService.NotifyEncryptionCompleteAsync(workItem.SenderWallet,
                new EncryptionSignal { OperationId = operationId, PercentComplete = 100, Status = EncryptionStatuses.Complete },
                userId: workItem.UserId, ct: ct);

            // Store persistent activity event for disconnected users (T047)
            await StoreActivityEventAsync(scope.ServiceProvider, workItem, txHash, success: true, error: null);

            activity?.SetTag("encryption.tx_hash", txHash);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation(
                "Encryption operation {OperationId} completed. Transaction: {TxHash}",
                operationId, txHash);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            var op = await _operationStore.GetByIdAsync(operationId);
            if (op != null)
            {
                op.Status = EncryptionOpStatus.Failed;
                op.Error = "Operation cancelled";
                op.CompletedAt = DateTimeOffset.UtcNow;
                await _operationStore.UpdateAsync(op);
            }
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Encryption operation {OperationId} failed", operationId);

            try
            {
                await HandleFailureAsync(operationId, workItem.SenderWallet,
                    ex.Message, null, null,
                    notificationService, scope.ServiceProvider, workItem, ct);
            }
            catch (Exception notifyEx)
            {
                _logger.LogError(notifyEx, "Failed to send failure notification for {OperationId}", operationId);
            }
        }
    }

    private async Task UpdateOperationStepAsync(
        string operationId, EncryptionOpStatus status, int step, string stepName, int percent)
    {
        var op = await _operationStore.GetByIdAsync(operationId);
        if (op != null)
        {
            op.Status = status;
            op.CurrentStep = step;
            op.StepName = stepName;
            op.PercentComplete = percent;
            await _operationStore.UpdateAsync(op);
        }
    }

    /// <summary>
    /// Feature 108 — fans an encrypted, signed submission out to the register's source peers so a
    /// replica-origin submission reaches the owner for sealing. Mirrors
    /// <c>ActionExecutionService.DistributeSubmissionAsync</c>: best-effort, never throws; a fan-out
    /// failure degrades to validator-only (logged) rather than failing the operation.
    /// </summary>
    private async Task<DistributeTransactionResult> DistributeEncryptedSubmissionAsync(
        IPeerServiceClient peerClient,
        string registerId,
        Sorcha.ServiceClients.Validator.TransactionSubmission submission,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(
                submission,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return await peerClient.DistributeTransactionAsync(registerId, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Feature 108 — peer fan-out errored for encrypted submission on register {RegisterId} " +
                "(tx {TxId}); falling back to validator-only. Subscriber nodes depend on this path to reach the owner.",
                registerId, submission.TransactionId);
            return new DistributeTransactionResult(0, 0, LocallyOwned: false);
        }
    }

    private async Task HandleFailureAsync(
        string operationId, string senderWallet, string error, string? failedRecipient, int? step,
        INotificationService notificationService, IServiceProvider serviceProvider,
        EncryptionWorkItem workItem, CancellationToken ct)
    {
        var op = await _operationStore.GetByIdAsync(operationId);
        if (op != null)
        {
            op.Status = EncryptionOpStatus.Failed;
            op.Error = error;
            op.FailedRecipient = failedRecipient;
            op.CompletedAt = DateTimeOffset.UtcNow;
            await _operationStore.UpdateAsync(op);
        }

        await notificationService.NotifyEncryptionFailedAsync(senderWallet,
            new EncryptionSignal { OperationId = operationId, PercentComplete = op?.PercentComplete ?? 0, Status = EncryptionStatuses.Failed },
            userId: workItem.UserId, ct: ct);

        await StoreActivityEventAsync(serviceProvider, workItem, null, success: false, error: error);

        _logger.LogWarning("Encryption operation {OperationId} failed: {Error}", operationId, error);
    }

    /// <summary>
    /// Poll the Register Service until the transaction is confirmed in a docket,
    /// then advance the workflow instance to the next action(s).
    /// </summary>
    private async Task WaitForConfirmationAndAdvanceInstanceAsync(
        AsyncServiceScope scope, EncryptionWorkItem workItem, string txId, CancellationToken ct)
    {
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IInstanceStore>();
        var confirmationOptions = scope.ServiceProvider.GetRequiredService<IOptions<TransactionConfirmationOptions>>().Value;

        // Poll for confirmation
        var deadline = DateTimeOffset.UtcNow + confirmationOptions.Timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var confirmedTx = await registerClient.GetTransactionAsync(workItem.RegisterId, txId, ct);
            if (confirmedTx != null)
            {
                _logger.LogInformation(
                    "Encrypted transaction {TxId} confirmed in docket {DocketNumber} for register {RegisterId}",
                    txId, confirmedTx.DocketNumber, workItem.RegisterId);
                break;
            }

            if (DateTimeOffset.UtcNow + confirmationOptions.PollInterval >= deadline)
            {
                _logger.LogWarning(
                    "Encrypted transaction {TxId} not confirmed within {Timeout}s for register {RegisterId}. Instance will not advance.",
                    txId, confirmationOptions.Timeout.TotalSeconds, workItem.RegisterId);
                return;
            }

            await Task.Delay(confirmationOptions.PollInterval, ct);
        }

        // Update accumulated data on instance
        var instance = await instanceStore.GetAsync(workItem.InstanceId, ct);
        if (instance == null)
        {
            _logger.LogError("Instance {InstanceId} not found for post-confirmation advancement", workItem.InstanceId);
            return;
        }

        foreach (var kvp in workItem.MergedData.Where(kvp => workItem.AllowedAccumulatedFields.Contains(kvp.Key)))
        {
            instance.AccumulatedData[kvp.Key] = kvp.Value;
        }

        // Generate instance reference on first action (idempotent)
        if (!instance.Metadata.ContainsKey("instanceReference"))
        {
            var actionResolver = scope.ServiceProvider.GetRequiredService<IActionResolverService>();
            var blueprint = await actionResolver.GetBlueprintAsync(instance.BlueprintId, ct);
            if (blueprint != null)
            {
                var instanceRef = Sorcha.Blueprint.Engine.Implementation.InstanceReferenceGenerator.Generate(
                    blueprint.InstanceReference,
                    instance.AccumulatedData,
                    instance.Id,
                    blueprint.Title);
                instance.Metadata["instanceReference"] = instanceRef;
                _logger.LogInformation(
                    "Generated instance reference {Reference} for instance {InstanceId} (async path)",
                    instanceRef, instance.Id);
            }
        }

        // Advance instance state (same logic as ActionExecutionService.ApplyInstanceStateChanges)
        instance.CurrentActionIds.Remove(workItem.ActionId);
        foreach (var nextAction in workItem.RoutingResult.NextActions)
        {
            if (!instance.CurrentActionIds.Contains(nextAction.ActionId))
            {
                instance.CurrentActionIds.Add(nextAction.ActionId);
            }

            if (!string.IsNullOrEmpty(nextAction.BranchId) &&
                !instance.ActiveBranches.Any(b => b.Id == nextAction.BranchId))
            {
                instance.ActiveBranches.Add(new Branch
                {
                    Id = nextAction.BranchId,
                    CurrentActionId = nextAction.ActionId,
                    State = BranchState.Active
                });
            }
        }

        instance.LastTransactionId = txId;
        instance.CompletedActionCount++;
        instance.FirstTransactionId ??= txId;

        if (instance.CurrentActionIds.Count == 0)
        {
            instance.State = InstanceState.Completed;
            instance.CompletedAt = DateTimeOffset.UtcNow;
        }

        await instanceStore.UpdateAsync(instance, ct);
        _logger.LogInformation(
            "Instance {InstanceId} advanced after encrypted action {ActionId}. Next actions: [{NextActions}]",
            workItem.InstanceId, workItem.ActionId,
            string.Join(", ", instance.CurrentActionIds));

        // B-15 — notify the next-action participants and emit workflow-complete
        // when the chain has run out. The inline ActionExecutionService path
        // calls NotifyParticipantsAsync at step 15; the async-encrypted path
        // skipped this, so a user awaiting an action from someone whose
        // submission went through the encrypted pipeline never received the
        // real-time SignalR push and saw "All Caught Up" until manual refresh.
        // Try/catch swallow — a notification failure must NOT roll back the
        // committed instance state.
        try
        {
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            foreach (var nextAction in workItem.RoutingResult.NextActions)
            {
                string? walletAddress = null;
                instance.ParticipantWallets?.TryGetValue(nextAction.ParticipantId, out walletAddress);

                await notificationService.NotifyActionAvailableAsync(
                    instance.Id,
                    walletAddress,
                    actionId: nextAction.ActionId.ToString(),
                    ct: ct);
            }

            if (workItem.RoutingResult.NextActions.Count == 0)
            {
                var walletAddresses = instance.ParticipantWallets?.Values
                    .Where(w => !string.IsNullOrEmpty(w))
                    .Distinct()
                    ?? [];

                await notificationService.NotifyWorkflowCompletedAsync(
                    instance.Id,
                    walletAddresses!,
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "NotifyParticipantsAsync (async-encrypted path) failed for instance {InstanceId} after action {ActionId}; instance state is committed but next-participant SignalR push was missed",
                workItem.InstanceId, workItem.ActionId);
        }
    }

    private async Task StoreActivityEventAsync(
        IServiceProvider serviceProvider, EncryptionWorkItem workItem,
        string? txHash, bool success, string? error)
    {
        try
        {
            var eventClient = serviceProvider.GetService<IEventServiceClient>();
            if (eventClient == null) return;

            var request = new CreateActivityEventRequest(
                OrganizationId: Guid.Empty, // System-level event
                UserId: Guid.Empty,
                EventType: success ? "EncryptionComplete" : "EncryptionFailed",
                Severity: success ? "Info" : "Error",
                Title: success
                    ? $"Encryption completed for action {workItem.ActionId}"
                    : $"Encryption failed for action {workItem.ActionId}",
                Message: success
                    ? $"Transaction {txHash} submitted for instance {workItem.InstanceId}"
                    : $"Encryption failed for instance {workItem.InstanceId}: {error}",
                SourceService: "Blueprint.EncryptionPipeline",
                EntityId: workItem.OperationId,
                EntityType: "EncryptionOperation");

            await eventClient.CreateEventAsync(request);
        }
        catch (Exception ex)
        {
            // Best-effort: don't fail the pipeline if event storage fails
            _logger.LogWarning(ex, "Failed to store activity event for operation {OperationId}", workItem.OperationId);
        }
    }
}
