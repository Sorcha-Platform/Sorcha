// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Threading.Channels;
using ActivityEvent = Sorcha.Blueprint.Service.Models.ActivityEvent;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
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

        try
        {
            // Step 1: Resolving Keys
            await UpdateOperationStepAsync(operationId, EncryptionOpStatus.ResolvingKeys,
                StepResolvingKeys, "Resolving recipient keys", 10);
            await notificationService.NotifyEncryptionProgressAsync(workItem.SenderWallet,
                new EncryptionProgressNotification
                {
                    OperationId = operationId,
                    Step = StepResolvingKeys,
                    StepName = "Resolving recipient keys",
                    TotalSteps = TotalSteps,
                    PercentComplete = 10
                }, ct);

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
                new EncryptionProgressNotification
                {
                    OperationId = operationId,
                    Step = StepEncrypting,
                    StepName = "Encrypting payloads",
                    TotalSteps = TotalSteps,
                    PercentComplete = 30
                }, ct);

            var encryptionResult = await encryptionPipeline.EncryptDisclosedPayloadsAsync(
                workItem.DisclosureGroups, ct);

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
                new EncryptionProgressNotification
                {
                    OperationId = operationId,
                    Step = StepBuildingTransaction,
                    StepName = "Building transaction",
                    TotalSteps = TotalSteps,
                    PercentComplete = 60
                }, ct);

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

            // Step 4: Signing and Submitting
            await UpdateOperationStepAsync(operationId, EncryptionOpStatus.Submitting,
                StepSubmitting, "Signing and submitting", 80);
            await notificationService.NotifyEncryptionProgressAsync(workItem.SenderWallet,
                new EncryptionProgressNotification
                {
                    OperationId = operationId,
                    Step = StepSubmitting,
                    StepName = "Signing and submitting",
                    TotalSteps = TotalSteps,
                    PercentComplete = 80
                }, ct);

            var signResult = await walletClient.SignTransactionAsync(
                workItem.SenderWallet,
                transaction.SigningData,
                derivationPath: null,
                isPreHashed: false,
                ct);

            transaction.SenderWallet = workItem.SenderWallet;
            transaction.Signature = signResult.Signature;

            var submission = transaction.ToTransactionSubmission(signResult);
            var validatorResult = await validatorClient.SubmitTransactionAsync(submission, ct);

            if (!validatorResult.Success)
            {
                await HandleFailureAsync(operationId, workItem.SenderWallet,
                    $"Validator rejected transaction: [{validatorResult.ErrorCode}] {validatorResult.ErrorMessage}",
                    null, StepSubmitting,
                    notificationService, scope.ServiceProvider, workItem, ct);
                return;
            }

            // Complete
            var txHash = transaction.TxId;
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
                new EncryptionCompleteNotification
                {
                    OperationId = operationId,
                    TransactionHash = txHash
                }, ct);

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
            new EncryptionFailedNotification
            {
                OperationId = operationId,
                Error = error,
                FailedRecipient = failedRecipient,
                Step = step
            }, ct);

        await StoreActivityEventAsync(serviceProvider, workItem, null, success: false, error: error);

        _logger.LogWarning("Encryption operation {OperationId} failed: {Error}", operationId, error);
    }

    private async Task StoreActivityEventAsync(
        IServiceProvider serviceProvider, EncryptionWorkItem workItem,
        string? txHash, bool success, string? error)
    {
        try
        {
            var eventService = serviceProvider.GetService<IEventService>();
            if (eventService == null) return;

            var activityEvent = new ActivityEvent
            {
                OrganizationId = Guid.Empty, // System-level event
                UserId = Guid.Empty,
                EventType = success ? "EncryptionComplete" : "EncryptionFailed",
                Severity = success ? EventSeverity.Info : EventSeverity.Error,
                Title = success
                    ? $"Encryption completed for action {workItem.ActionId}"
                    : $"Encryption failed for action {workItem.ActionId}",
                Message = success
                    ? $"Transaction {txHash} submitted for instance {workItem.InstanceId}"
                    : $"Encryption failed for instance {workItem.InstanceId}: {error}",
                SourceService = "Blueprint.EncryptionPipeline",
                EntityId = workItem.OperationId,
                EntityType = "EncryptionOperation",
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };

            await eventService.CreateEventAsync(activityEvent);
        }
        catch (Exception ex)
        {
            // Best-effort: don't fail the pipeline if event storage fails
            _logger.LogWarning(ex, "Failed to store activity event for operation {OperationId}", workItem.OperationId);
        }
    }
}
