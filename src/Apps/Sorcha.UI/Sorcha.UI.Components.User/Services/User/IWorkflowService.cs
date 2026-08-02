// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for managing workflow instances and user actions.
/// </summary>
public interface IWorkflowService
{
    // Feature 186 (#1163): GetMyWorkflowsAsync is GONE, not retargeted. It called
    // GET /api/instances, which returns the raw Instance model, and bound it to
    // WorkflowInstanceViewModel — a shape that does not match. It yielded an id and two timestamps;
    // `Status` silently read "active" for every instance, rejected ones included, because the server
    // sends `state` and sends it as an integer. It had zero callers, and anyone who tried to build
    // on it got a page that looked empty and moved on. Deleting it closes that trap rather than
    // leaving a plausible-looking method to be found again.
    //
    // The citizen surface it was meant to serve is IMyApplicationsService, over /api/me/applications.

    /// <summary>
    /// Gets a specific workflow instance.
    /// </summary>
    Task<WorkflowInstanceViewModel?> GetWorkflowAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending actions for the current user.
    /// </summary>
    Task<List<PendingActionViewModel>> GetPendingActionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new workflow instance for a blueprint in a register.
    /// </summary>
    Task<WorkflowInstanceViewModel?> CreateInstanceAsync(string blueprintId, string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits action data for a workflow step with full request context.
    /// </summary>
    Task<ActionSubmissionResultViewModel?> SubmitActionExecuteAsync(ActionExecuteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits action data for a workflow step (legacy).
    /// </summary>
    Task<bool> SubmitActionAsync(ActionSubmissionViewModel submission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects an action with a reason.
    /// </summary>
    Task<bool> RejectActionAsync(string instanceId, string actionId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a blueprint definition including action schemas.
    /// Used to populate the Execute Action form.
    /// </summary>
    Task<Sorcha.Blueprint.Models.Blueprint?> GetBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default);
}
