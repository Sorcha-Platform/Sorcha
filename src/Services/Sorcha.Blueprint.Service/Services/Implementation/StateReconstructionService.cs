// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Models;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
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
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly ILogger<StateReconstructionService> _logger;
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.Service.StateReconstruction");

    public StateReconstructionService(
        IRegisterServiceClient registerClient,
        IWalletServiceClient walletClient,
        ISymmetricCrypto symmetricCrypto,
        ILogger<StateReconstructionService> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
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

        // Determine the register's DevMode posture once. DevMode registers store payloads as
        // PLAINTEXT (encryption skipped); the reconstruction below reads them directly. This is the
        // ONLY input that unlocks the plaintext path — a Normal register must never read plaintext
        // payloads (that would bypass field-level encryption). Null/unreachable → false (fail closed
        // to the encrypted/decrypt path).
        bool registerDevMode = false;
        try
        {
            var register = await _registerClient.GetRegisterAsync(registerId, cancellationToken);
            registerDevMode = register?.DevMode ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve DevMode for register {RegisterId} during reconstruction — defaulting to encrypted path",
                registerId);
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

            // Decrypt the payload using delegated access (or read plaintext for DevMode registers)
            var decryptedData = await DecryptTransactionPayloadAsync(
                tx, delegationToken, participantWallets, registerDevMode, cancellationToken);

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
    /// <remarks>
    /// Feature 137 — the production transaction format is the <b>disclosure-group envelope</b>
    /// (<c>ITransactionBuilderService.BuildEncryptedActionTransactionAsync</c>): <c>Data</c> is a
    /// JSON document carrying <c>encryptedPayloads[]</c>, each group symmetric-encrypted
    /// (XChaCha20-Poly1305) with the symmetric key wrapped per recipient under
    /// <c>wrappedKeys[]</c>. To recover a prior action's plaintext we find the group sealed to one
    /// of our local participant wallets, unwrap its symmetric key via the Wallet Service, then
    /// symmetric-decrypt the group ciphertext — mirroring the holder-side path in
    /// <c>InboundCredentialDetector.TryDecryptGroupForWalletAsync</c>.
    /// <para>
    /// This is what makes <b>cross-node</b> issuance work: on the owner node the acting
    /// participant's wallet (e.g. the verification-analyst) is local, so the owner can reconstruct
    /// the citizen's submitted action-1 payload (credential claims + carried holder keys) from the
    /// register — same-node never needed this because it reads the authoritative instance's
    /// <c>AccumulatedData</c>. The legacy <c>WalletAccess</c> + whole-<c>Data</c> path is retained
    /// as a fallback for any pre-disclosure-group transaction.
    /// </para>
    /// </remarks>
    private async Task<JsonElement?> DecryptTransactionPayloadAsync(
        Sorcha.Register.Models.TransactionModel transaction,
        string delegationToken,
        Dictionary<string, string> participantWallets,
        bool registerDevMode,
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

            // Convert Base64/Base64url data to bytes
            var encryptedBytes = Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(encryptedPayload.Data);

            // Primary path: disclosure-group envelope ({ …, "encryptedPayloads": [ … ] }).
            JsonDocument? envelope = null;
            try
            {
                envelope = JsonDocument.Parse(encryptedBytes);
            }
            catch (JsonException)
            {
                // Not a JSON envelope — fall through to the legacy whole-Data path below.
            }

            if (envelope is not null)
            {
                using (envelope)
                {
                    // DevMode plaintext path — STRICTLY gated on registerDevMode. A DevMode register
                    // stores payloads unencrypted as { …, "payloads": { "<wallet>": { …fields… } } }.
                    // We merge the disclosed field objects (preferring this instance's participant
                    // wallets) and return them as the prior action's data — no decryption. This branch
                    // is unreachable for a Normal register, so production payloads are never read as
                    // plaintext even if a "payloads" envelope were somehow present (it falls through to
                    // the encrypted/decrypt paths and fails closed).
                    if (registerDevMode
                        && envelope.RootElement.ValueKind == JsonValueKind.Object
                        && envelope.RootElement.TryGetProperty("payloads", out var devPayloadsEl)
                        && devPayloadsEl.ValueKind == JsonValueKind.Object)
                    {
                        var plaintext = MergeDevModePlaintextPayloads(transaction.TxId, devPayloadsEl, participantWallets);
                        if (plaintext.HasValue)
                            return plaintext;

                        _logger.LogWarning(
                            "Transaction {TxId}: DevMode register but no usable plaintext payload entry — prior-action state will be incomplete",
                            transaction.TxId);
                        return null;
                    }

                    if (envelope.RootElement.ValueKind == JsonValueKind.Object
                        && envelope.RootElement.TryGetProperty("encryptedPayloads", out var groupsEl)
                        && groupsEl.ValueKind == JsonValueKind.Array)
                    {
                        var decrypted = await TryDecryptDisclosureGroupsAsync(
                            transaction.TxId, groupsEl, participantWallets, delegationToken, cancellationToken);

                        if (decrypted.HasValue)
                            return decrypted;

                        _logger.LogWarning(
                            "Transaction {TxId}: no disclosure group decryptable by a local participant wallet ({WalletCount} candidate(s)) — prior-action state will be incomplete",
                            transaction.TxId, participantWallets.Count);
                        return null;
                    }
                }
            }

            // Legacy fallback: the whole Data blob is the recipient-encrypted payload, keyed by
            // the first WalletAccess entry.
            var recipientAddress = encryptedPayload.WalletAccess?.FirstOrDefault();
            if (string.IsNullOrEmpty(recipientAddress))
            {
                _logger.LogWarning(
                    "Transaction {TxId} payload is neither a disclosure-group envelope nor has a WalletAccess recipient — cannot decrypt",
                    transaction.TxId);
                return null;
            }

            var decryptedBytes = await _walletClient.DecryptWithDelegationAsync(
                recipientAddress,
                encryptedBytes,
                delegationToken,
                cancellationToken);

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
    /// Merges the plaintext disclosed-field objects from a DevMode <c>payloads</c> envelope into a
    /// single object — the prior action's reconstructed data. Prefers entries keyed by one of this
    /// instance's participant wallets (the disclosed views this node is entitled to); if none match,
    /// falls back to merging every entry (DevMode is a plaintext debug posture with no on-ledger
    /// encryption boundary). Returns null when there is nothing to merge. Only ever invoked for a
    /// DevMode register — see the gate in <see cref="DecryptTransactionPayloadAsync"/>.
    /// </summary>
    private JsonElement? MergeDevModePlaintextPayloads(
        string txId,
        JsonElement payloadsObj,
        Dictionary<string, string> participantWallets)
    {
        var localWallets = new HashSet<string>(participantWallets.Values, StringComparer.OrdinalIgnoreCase);
        var merged = new JsonObject();
        var matchedParticipantEntry = false;

        // First pass: entries keyed by a participant wallet bound to this instance.
        foreach (var entry in payloadsObj.EnumerateObject())
        {
            if (!localWallets.Contains(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var field in entry.Value.EnumerateObject())
                merged[field.Name] = JsonNode.Parse(field.Value.GetRawText());
            matchedParticipantEntry = true;
        }

        // Fallback: no participant-keyed entry — merge every object entry (debug-mode plaintext,
        // no encryption boundary to honour).
        if (!matchedParticipantEntry)
        {
            foreach (var entry in payloadsObj.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var field in entry.Value.EnumerateObject())
                    merged[field.Name] = JsonNode.Parse(field.Value.GetRawText());
            }
        }

        if (merged.Count == 0)
        {
            _logger.LogDebug("Transaction {TxId}: DevMode payloads envelope had no mergeable field objects", txId);
            return null;
        }

        using var doc = JsonDocument.Parse(merged.ToJsonString());
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Walks an <c>encryptedPayloads[]</c> array and decrypts the first group sealed to one of the
    /// instance's participant wallets. The symmetric key is unwrapped via the Wallet Service
    /// (<see cref="IWalletServiceClient.DecryptWithDelegationAsync"/>) — service-to-service auth lets
    /// the owner node unwrap any wallet it custodies; non-local wallets fail the unwrap and are
    /// skipped. The group ciphertext is then symmetric-decrypted (XChaCha20-Poly1305). Returns the
    /// decrypted payload JSON, or null if no group is decryptable here.
    /// </summary>
    private async Task<JsonElement?> TryDecryptDisclosureGroupsAsync(
        string txId,
        JsonElement groupsEl,
        Dictionary<string, string> participantWallets,
        string delegationToken,
        CancellationToken ct)
    {
        var candidateWallets = new HashSet<string>(participantWallets.Values, StringComparer.OrdinalIgnoreCase);
        if (candidateWallets.Count == 0)
        {
            _logger.LogDebug("Transaction {TxId}: no participant wallets to decrypt as", txId);
            return null;
        }

        foreach (var group in groupsEl.EnumerateArray())
        {
            if (!group.TryGetProperty("wrappedKeys", out var wrappedKeysEl)
                || wrappedKeysEl.ValueKind != JsonValueKind.Array
                || !group.TryGetProperty("ciphertext", out var ctEl)
                || !group.TryGetProperty("nonce", out var nonceEl))
            {
                continue;
            }

            byte[] ciphertext;
            byte[] nonce;
            try
            {
                ciphertext = Convert.FromBase64String(ctEl.GetString()!);
                nonce = Convert.FromBase64String(nonceEl.GetString()!);
            }
            catch
            {
                continue;
            }

            foreach (var wk in wrappedKeysEl.EnumerateArray())
            {
                var wkAddress = wk.TryGetProperty("walletAddress", out var waEl) ? waEl.GetString() : null;
                if (string.IsNullOrEmpty(wkAddress) || !candidateWallets.Contains(wkAddress))
                    continue;

                if (!wk.TryGetProperty("encryptedKey", out var ekEl))
                    continue;

                byte[] encryptedKey;
                try
                {
                    encryptedKey = Convert.FromBase64String(ekEl.GetString()!);
                }
                catch
                {
                    continue;
                }

                byte[] symmetricKey;
                try
                {
                    // Unwrap the group's symmetric key with this wallet's private key. On the owner
                    // node only the acting participant's wallet is local; a non-local wallet (e.g. the
                    // cross-node citizen) throws here and we move on to the next wrapped key.
                    symmetricKey = await _walletClient.DecryptWithDelegationAsync(
                        wkAddress, encryptedKey, delegationToken, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Transaction {TxId}: wrapped-key unwrap failed for wallet {Wallet} (likely not local) — trying next recipient",
                        txId, wkAddress);
                    continue;
                }

                using var cipherModel = new SymmetricCiphertext
                {
                    Data = ciphertext,
                    Key = symmetricKey,
                    IV = nonce,
                    Type = EncryptionType.XCHACHA20_POLY1305,
                };

                var decrypt = await _symmetricCrypto.DecryptAsync(cipherModel, ct);
                if (!decrypt.IsSuccess || decrypt.Value is null)
                {
                    _logger.LogDebug(
                        "Transaction {TxId}: symmetric decrypt failed for wallet {Wallet}: {Error}",
                        txId, wkAddress, decrypt.ErrorMessage);
                    continue;
                }

                try
                {
                    return JsonSerializer.Deserialize<JsonElement>(decrypt.Value);
                }
                catch (JsonException)
                {
                    _logger.LogDebug("Transaction {TxId}: decrypted payload for wallet {Wallet} is not valid JSON", txId, wkAddress);
                    continue;
                }
            }
        }

        return null;
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
