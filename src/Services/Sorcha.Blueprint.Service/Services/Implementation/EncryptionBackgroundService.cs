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
        var encryptionInboxWriter = scope.ServiceProvider.GetRequiredService<IEncryptionInboxWriter>();

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

            // Feature 145: assemble + sender-sign the full RoutingDecision (the encrypted path's
            // analog of ActionExecutionService step 10d) and carry it on the tx's clear metadata.
            // The validator validates it (VAL_ROUTING_*) and it rides to the seal as the typed
            // RoutingDecision every node's InstanceProjector folds — replacing the legacy singular
            // nextActionId hint. Without this, encrypted-register actions carried NO routing decision
            // at all (only the now-removed nextActionId), so the decision never reached the seal.
            // Feature 194: the encrypted path must stamp the same pin as the plaintext one, or an
            // encrypted register's instances would be unpinned and every action on them would fold
            // through the pre-feature fallback — i.e. keep the old "always latest" behaviour on
            // exactly the registers that carry the most sensitive workflows.
            var pinnedExecDefHash = string.IsNullOrWhiteSpace(instance.BlueprintExecDefHash)
                ? null
                : instance.BlueprintExecDefHash;
            if (pinnedExecDefHash is null)
            {
                _logger.LogWarning(
                    "Instance {InstanceId} has no pinned blueprint definition; encrypted action {ActionId} " +
                    "will be carried unpinned and folded via the pre-Feature-194 fallback.",
                    workItem.InstanceId, workItem.ActionId);
            }

            var routingDecision = new Sorcha.Register.Models.RoutingDecision
            {
                CompletedActionId = workItem.ActionId,
                NextActions = workItem.RoutingResult.NextActions
                    .Select(n => new Sorcha.Register.Models.ActionRef { ActionId = n.ActionId, BranchKey = n.BranchId })
                    .ToList(),
                BlueprintExecDefHash = pinnedExecDefHash,
                Attestation = new Sorcha.Register.Models.Attestation
                {
                    Kind = Sorcha.Register.Models.AttestationKind.SenderSigned,
                },
            };
            var routingSignResult = await walletClient.SignTransactionAsync(
                workItem.SenderWallet, routingDecision.ComputeSignableBytes(),
                derivationPath: null, isPreHashed: false, ct);
            routingDecision.Attestation.Signature = Convert.ToBase64String(routingSignResult.Signature);
            transaction.Metadata["routingDecision"] = JsonSerializer.Serialize(
                routingDecision, Sorcha.Register.Models.RegisterSerializationOptions.Canonical);

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

            // Feature 108 / 137 / 145 — submit to the local validator (data-validation + mempool
            // ingress; seals iff this node is on the roster) AND a carrier-aware peer fan-out (only
            // peers that carry the register; no seed/topology dial). Nothing waits for sealing — the
            // InstanceProjector advances every node when the docket seals.
            var validatorResult = await validatorClient.SubmitTransactionAsync(submission, ct);
            var distributeResult = peerClient is null
                ? new DistributeTransactionResult(0, 0)
                : await DistributeEncryptedSubmissionAsync(peerClient, workItem.RegisterId, submission, ct);

            _logger.LogInformation(
                "Encrypted submission {TxId} for register {RegisterId}: validator success={ValidatorSuccess}, carriers targets={Targets} accepted={Accepted}",
                transaction.TxId, workItem.RegisterId, validatorResult.Success,
                distributeResult.TargetPeerCount, distributeResult.AcceptedCount);

            // Fail only when the local validator rejected AND no carrier accepted — a subscriber's
            // local validator never seals (off-roster), so a carrier acceptance is the ingress signal.
            if (!validatorResult.Success && distributeResult.AcceptedCount == 0)
            {
                await HandleFailureAsync(operationId, workItem.SenderWallet,
                    $"Validator rejected transaction: [{validatorResult.ErrorCode}] {validatorResult.ErrorMessage} — and no carrier accepted the fan-out",
                    null, StepSubmitting,
                    notificationService, scope.ServiceProvider, workItem, ct);
                return;
            }

            // Feature 145: the encrypted submission is now on the ledger. The InstanceProjector folds
            // the sealed docket on every node to advance the instance — the background service no longer
            // advances instance state itself (that was a double-write once the projector became the
            // single writer) and no longer emits the next-participant notify (the projector fires that
            // post-fold). The instance reference was generated pre-submit in ActionExecutionService.
            var txHash = transaction.TxId;

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

            if (Guid.TryParse(workItem.UserId, out var successUserId))
            {
                try
                {
                    await encryptionInboxWriter.WriteEncryptionCompleteAsync(successUserId, workItem.OperationId, ct);
                }
                catch (Exception inboxEx)
                {
                    _logger.LogWarning(inboxEx, "EncryptionInboxWriter — failed to emit encryption-complete inbox entry for {UserId}", workItem.UserId);
                }
            }

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
                "(tx {TxId}); falling back to validator-only. Subscriber nodes depend on this path to reach a carrier.",
                registerId, submission.TransactionId);
            return new DistributeTransactionResult(0, 0);
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

        if (Guid.TryParse(workItem.UserId, out var failUserId))
        {
            var inboxWriter = serviceProvider.GetRequiredService<IEncryptionInboxWriter>();
            try
            {
                await inboxWriter.WriteEncryptionFailedAsync(failUserId, workItem.OperationId, ct);
            }
            catch (Exception inboxEx)
            {
                _logger.LogWarning(inboxEx, "EncryptionInboxWriter — failed to emit encryption-failed inbox entry for {UserId}", workItem.UserId);
            }
        }

        _logger.LogWarning("Encryption operation {OperationId} failed: {Error}", operationId, error);
    }

}
