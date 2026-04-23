// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using Sorcha.Blueprint.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Implements state reconstruction from prior workflow transactions.
/// Coordinates with Register Service to fetch transactions and Wallet Service to decrypt payloads.
/// </summary>
public class StateReconstructionService : IStateReconstructionService
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly ILogger<StateReconstructionService> _logger;
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.Service.StateReconstruction");

    public StateReconstructionService(
        IRegisterServiceClient registerClient,
        IWalletServiceClient walletClient,
        ILogger<StateReconstructionService> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<AccumulatedState> ReconstructAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        string instanceId,
        int currentActionId,
        string registerId,
        string delegationToken,
        Dictionary<string, string> participantWallets,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ReconstructState");
        activity?.SetTag("blueprint.id", blueprint.Id);
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("action.id", currentActionId);

        _logger.LogDebug("Reconstructing state for instance {InstanceId}, action {ActionId}",
            instanceId, currentActionId);

        // Get the current action definition
        var currentAction = blueprint.Actions?.FirstOrDefault(a => a.Id == currentActionId);
        if (currentAction == null)
        {
            throw new InvalidOperationException($"Action {currentActionId} not found in blueprint {blueprint.Id}");
        }

        // Determine which prior actions we need data from
        var requiredActionIds = GetRequiredActionIds(blueprint, currentAction);

        if (requiredActionIds.Count == 0)
        {
            _logger.LogDebug("No prior actions required for action {ActionId}", currentActionId);
            return new AccumulatedState
            {
                ActionCount = 0
            };
        }

        // Fetch all transactions for this instance
        var transactions = await _registerClient.GetTransactionsByInstanceIdAsync(
            registerId, instanceId, cancellationToken);

        if (transactions.Count == 0)
        {
            _logger.LogDebug("No transactions found for instance {InstanceId}", instanceId);
            return new AccumulatedState
            {
                ActionCount = 0
            };
        }

        // Decrypt and accumulate data from required actions
        var actionData = new Dictionary<string, JsonElement>();
        var branchStates = new Dictionary<string, BranchState>();
        string? previousTransactionId = null;

        foreach (var tx in transactions.OrderBy(t => t.TimeStamp))
        {
            // Track the chain head on every instance tx — including rejections and
            // actions whose data isn't required for the current step. The chain head
            // is a linkage concern, distinct from the "what data do I need" filter
            // below. If we only advanced prev on required-action txs, a rejection
            // (whose actionId is the rejected action, often not in requiredActionIds
            // for the route-back target) would leave prev stale, and the next
            // submission would trip VAL_CHAIN_FORK on a non-unique previousTxId.
            previousTransactionId = tx.TxId;

            // Extract action ID from transaction metadata
            var txActionId = ExtractActionIdFromTransaction(tx);
            if (txActionId == null)
            {
                continue;
            }

            // Only accumulate state from prior actions whose data is required
            if (!requiredActionIds.Contains(txActionId.Value))
            {
                continue;
            }

            // Decrypt the payload using delegated access
            var decryptedData = await DecryptTransactionPayloadAsync(
                tx, delegationToken, participantWallets, cancellationToken);

            if (decryptedData.HasValue)
            {
                actionData[txActionId.Value.ToString()] = decryptedData.Value;
            }

            // Track branch states if present
            var branchId = ExtractBranchIdFromTransaction(tx);
            if (!string.IsNullOrEmpty(branchId))
            {
                branchStates[branchId] = BranchState.Active;
            }
        }

        var state = new AccumulatedState
        {
            ActionData = actionData,
            PreviousTransactionId = previousTransactionId,
            ActionCount = actionData.Count,
            BranchStates = branchStates
        };

        activity?.SetTag("state.action_count", state.ActionCount);
        _logger.LogInformation(
            "Reconstructed state for instance {InstanceId}: {ActionCount} actions, previous tx {PreviousTxId}",
            instanceId, state.ActionCount, state.PreviousTransactionId ?? "none");

        return state;
    }

    /// <inheritdoc/>
    public async Task<AccumulatedState> ReconstructForBranchAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        string instanceId,
        int currentActionId,
        string branchId,
        string registerId,
        string delegationToken,
        Dictionary<string, string> participantWallets,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ReconstructStateForBranch");
        activity?.SetTag("branch.id", branchId);

        _logger.LogDebug("Reconstructing state for branch {BranchId} in instance {InstanceId}",
            branchId, instanceId);

        // For branch reconstruction, we reconstruct the base state first
        var baseState = await ReconstructAsync(
            blueprint, instanceId, currentActionId, registerId,
            delegationToken, participantWallets, cancellationToken);

        // Filter to only include actions from this branch
        // Branch-specific filtering would be implemented here based on transaction metadata
        // For now, return the base state with branch context
        return baseState with
        {
            BranchStates = new Dictionary<string, BranchState>
            {
                [branchId] = BranchState.Active
            }
        };
    }

    /// <summary>
    /// Determines which prior action IDs are needed for state reconstruction.
    /// Uses blueprint-defined scope when available, otherwise uses all prior actions.
    /// </summary>
    private List<int> GetRequiredActionIds(Sorcha.Blueprint.Models.Blueprint blueprint, Sorcha.Blueprint.Models.Action currentAction)
    {
        // If the action specifies required prior actions, use those
        if (currentAction.RequiredPriorActions?.Any() == true)
        {
            return currentAction.RequiredPriorActions.ToList();
        }

        // Otherwise, include all actions that could have executed before this one
        // This is determined by analyzing the blueprint's action flow
        var priorActionIds = new List<int>();
        var visited = new HashSet<int>();

        // Find all actions that could route to the current action
        FindPriorActions(blueprint, currentAction.Id, priorActionIds, visited);

        return priorActionIds;
    }

    /// <summary>
    /// Recursively finds all actions that could precede the target action.
    /// </summary>
    private void FindPriorActions(Sorcha.Blueprint.Models.Blueprint blueprint, int targetActionId, List<int> priorActions, HashSet<int> visited)
    {
        if (blueprint.Actions == null) return;

        foreach (var action in blueprint.Actions)
        {
            if (visited.Contains(action.Id)) continue;

            // Check if this action routes to the target
            var routesToTarget = false;

            if (action.Routes != null)
            {
                routesToTarget = action.Routes.Any(r => r.NextActionIds?.Contains(targetActionId) == true);
            }
            else if (action.Condition != null)
            {
                // Legacy condition-based routing - check if it could evaluate to target
                // For now, assume it could
                routesToTarget = true;
            }

            if (routesToTarget && action.Id != targetActionId)
            {
                visited.Add(action.Id);
                priorActions.Add(action.Id);

                // Recursively find actions that precede this one
                FindPriorActions(blueprint, action.Id, priorActions, visited);
            }
        }
    }

    /// <summary>
    /// Decrypts a transaction payload using delegated access.
    /// </summary>
    private async Task<JsonElement?> DecryptTransactionPayloadAsync(
        Sorcha.Register.Models.TransactionModel transaction,
        string delegationToken,
        Dictionary<string, string> participantWallets,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the encrypted payload from the transaction
            var encryptedPayload = transaction.Payloads?.FirstOrDefault();
            if (encryptedPayload == null || string.IsNullOrEmpty(encryptedPayload.Data))
            {
                _logger.LogDebug("Transaction {TxId} has no payload to decrypt", transaction.TxId);
                return null;
            }

            // Determine which wallet to use for decryption
            // Use the first wallet from the access list
            var recipientAddress = encryptedPayload.WalletAccess?.FirstOrDefault();
            if (string.IsNullOrEmpty(recipientAddress))
            {
                _logger.LogWarning("Transaction {TxId} payload has no recipient address in WalletAccess", transaction.TxId);
                return null;
            }

            // Convert Base64/Base64url data to bytes
            var encryptedBytes = Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(encryptedPayload.Data);

            // Decrypt using delegated access
            var decryptedBytes = await _walletClient.DecryptWithDelegationAsync(
                recipientAddress,
                encryptedBytes,
                delegationToken,
                cancellationToken);

            // Parse the decrypted data as JSON
            var jsonDocument = JsonDocument.Parse(decryptedBytes);
            return jsonDocument.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt payload for transaction {TxId}", transaction.TxId);
            return null;
        }
    }

    /// <summary>
    /// Extracts the action ID from transaction metadata.
    /// </summary>
    private static int? ExtractActionIdFromTransaction(Sorcha.Register.Models.TransactionModel transaction)
    {
        // Action ID should be stored in transaction metadata
        if (transaction.MetaData?.ActionId.HasValue == true)
        {
            return (int)transaction.MetaData.ActionId.Value;
        }

        // Fallback to tracking data
        if (transaction.MetaData?.TrackingData?.TryGetValue("actionId", out var actionIdStr) == true)
        {
            if (int.TryParse(actionIdStr, out var actionId))
            {
                return actionId;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the branch ID from transaction metadata.
    /// </summary>
    private static string? ExtractBranchIdFromTransaction(Sorcha.Register.Models.TransactionModel transaction)
    {
        if (transaction.MetaData?.TrackingData?.TryGetValue("branchId", out var branchIdValue) == true)
        {
            return branchIdValue;
        }

        return null;
    }
}
