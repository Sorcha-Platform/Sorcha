// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// Storage interface for workflow instances
/// </summary>
public interface IInstanceStore
{
    /// <summary>
    /// Creates a new workflow instance
    /// </summary>
    /// <param name="instance">The instance to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created instance</returns>
    Task<Instance> CreateAsync(Instance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an instance by ID
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The instance if found, otherwise null</returns>
    Task<Instance?> GetAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing instance
    /// </summary>
    /// <param name="instance">The instance to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated instance</returns>
    Task<Instance> UpdateAsync(Instance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all instances for a blueprint
    /// </summary>
    /// <param name="blueprintId">The blueprint ID</param>
    /// <param name="state">Optional state filter</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of instances</returns>
    Task<IEnumerable<Instance>> GetByBlueprintAsync(
        string blueprintId,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all instances for a register
    /// </summary>
    /// <param name="registerId">The register ID</param>
    /// <param name="state">Optional state filter</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of instances</returns>
    Task<IEnumerable<Instance>> GetByRegisterAsync(
        string registerId,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets instances where a participant wallet is involved
    /// </summary>
    /// <param name="walletAddress">The wallet address</param>
    /// <param name="state">Optional state filter</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of instances</returns>
    Task<IEnumerable<Instance>> GetByParticipantWalletAsync(
        string walletAddress,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending action summaries for a participant wallet address.
    /// Returns active instances where the wallet is a participant and has pending actions.
    /// </summary>
    /// <param name="walletAddress">The wallet address</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of pending action summaries with blueprint context</returns>
    Task<IEnumerable<PendingActionSummary>> GetPendingActionsByWalletAsync(
        string walletAddress,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total count of pending actions for a wallet address.
    /// </summary>
    Task<int> GetPendingActionCountByWalletAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an instance
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of all instances
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total number of instances</returns>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of instances in a specific state
    /// </summary>
    /// <param name="state">The instance state to count</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of instances in the specified state</returns>
    Task<int> CountByStateAsync(InstanceState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Feature 106 — creates or overwrites a read-only mirror row for an instance
    /// observed via peer sync. The <see cref="Instance.IsReadOnlyMirror"/> flag is
    /// forced true regardless of caller input, and subsequent writes through
    /// <see cref="UpdateAsync"/> are rejected by the store; mirror rows can only
    /// be mutated via <see cref="UpdateMirrorAsync"/>.
    /// </summary>
    /// <remarks>
    /// Contract: <c>specs/106-register-native-credentials/contracts/instance-mirror-reconstructor.md</c>.
    /// Called exclusively by <c>InstanceMirrorReconstructor</c> when a
    /// <c>docket:confirmed</c> event references an instance whose participants
    /// include a locally-registered wallet. Implementations should upsert (no-op
    /// when the instance is already the authoritative row on this node).
    /// </remarks>
    Task<Instance> CreateMirrorAsync(Instance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Feature 106 — updates an existing read-only mirror row. Unlike
    /// <see cref="UpdateAsync"/>, this method is permitted on rows with
    /// <see cref="Instance.IsReadOnlyMirror"/> set. Throws when the target row is
    /// NOT a mirror (to prevent accidental mirror-write on an authoritative row).
    /// </summary>
    Task<Instance> UpdateMirrorAsync(Instance instance, CancellationToken cancellationToken = default);
}
