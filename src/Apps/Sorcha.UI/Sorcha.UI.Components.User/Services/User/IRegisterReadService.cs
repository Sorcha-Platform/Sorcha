// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
}
