// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Feature 186 (#1163) — reads the signed-in citizen's own applications: what they submitted, where
/// each one got to, and what was decided.
/// </summary>
/// <remarks>
/// Separate from <see cref="IWorkflowService"/> on purpose. That interface speaks the raw instance
/// model on <c>/api/instances</c> and serves the admin workflow surfaces; this one speaks the
/// citizen projection on <c>/api/me/applications</c>, where the decision wording is already resolved.
/// </remarks>
public interface IMyApplicationsService
{
    /// <summary>
    /// Lists the caller's applications, newest first, including finished ones.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of applications; empty when the caller has none.</returns>
    Task<PaginatedList<MyApplicationViewModel>> GetMyApplicationsAsync(
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one application with its step timeline.
    /// </summary>
    /// <param name="instanceId">The application id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The application, or null when it does not exist or is not the caller's.</returns>
    Task<MyApplicationDetailViewModel?> GetMyApplicationAsync(
        string instanceId, CancellationToken cancellationToken = default);
}
