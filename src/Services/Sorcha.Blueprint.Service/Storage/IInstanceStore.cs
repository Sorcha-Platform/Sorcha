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
    /// Gets all instances with optional state filter and pagination
    /// </summary>
    /// <param name="state">Optional state filter</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of instances and total count</returns>
    Task<(IEnumerable<Instance> Items, int TotalCount)> GetAllAsync(
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

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
    /// Deletes an instance
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteAsync(string instanceId, CancellationToken cancellationToken = default);
}
