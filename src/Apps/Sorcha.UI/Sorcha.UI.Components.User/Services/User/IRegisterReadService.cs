// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.LocalRelationship;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// User-facing register read operations. Consumed by pages that display
/// the registers a user can see — dashboard, register list, register detail,
/// new-submission flows. No governance, no policy, no admin.
/// </summary>
/// <remarks>
/// Split from <c>IRegisterService</c> as part of Feature 123 so user-facing
/// components can inject just the read surface without inheriting the admin
/// governance and policy methods. The admin half lives in
/// <see cref="IRegisterGovernanceService"/>.
/// </remarks>
public interface IRegisterReadService
{
    /// <summary>
    /// Gets all accessible registers for the current user's organisation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of registers.</returns>
    Task<IReadOnlyList<RegisterViewModel>> GetRegistersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single register by ID.
    /// </summary>
    /// <param name="registerId">Register identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Register details or null if not found.</returns>
    Task<RegisterViewModel?> GetRegisterAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the local node's derived relationship (ownership/role flags) for a register
    /// (Feature 108). Returns null when the register is not held locally or the read fails.
    /// </summary>
    /// <param name="registerId">Register identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local-relationship record, or null.</returns>
    Task<RegisterLocalRelationship?> GetLocalRelationshipAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sync-state view for a register (Feature 108). Returns null when the
    /// register is not held locally or the read fails.
    /// </summary>
    /// <param name="registerId">Register identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sync-state view, or null.</returns>
    Task<RegisterSyncStateView?> GetSyncStateAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of blueprints already published to a register. Returns 0 when none are
    /// published or the read fails (the count degrades gracefully — it never throws).
    /// </summary>
    /// <param name="registerId">Register identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published-blueprint count, or 0.</returns>
    Task<int> GetPublishedBlueprintCountAsync(
        string registerId,
        CancellationToken cancellationToken = default);
}
