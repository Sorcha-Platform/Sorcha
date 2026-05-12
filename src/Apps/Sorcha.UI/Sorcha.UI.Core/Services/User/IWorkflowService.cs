// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for managing workflow instances and user actions.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Gets workflow instances for the current user.
    /// </summary>
    Task<PaginatedList<WorkflowInstanceViewModel>> GetMyWorkflowsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

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
