// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Engine.Models;

/// <summary>
/// Represents a single routed action target in a parallel or single-route result.
/// </summary>
public record RoutedAction
{
    /// <summary>Target action ID.</summary>
    public string ActionId { get; init; } = "";

    /// <summary>Target participant ID, if specified by the route.</summary>
    public string? ParticipantId { get; init; }

    /// <summary>Unique branch identifier for parallel execution tracking.</summary>
    public string? BranchId { get; init; }

    /// <summary>The route ID that was matched to produce this action.</summary>
    public string? MatchedRouteId { get; init; }
}

/// <summary>
/// Result of routing determination indicating the next step in the workflow.
/// </summary>
/// <remarks>
/// The routing result is produced by evaluating routing conditions
/// against the action data to determine which participant should
/// perform the next action.
///
/// Four possible outcomes:
/// 1. Normal routing: NextActionId and NextParticipantId are set (single next action)
/// 2. Parallel routing: NextActions has multiple entries, IsParallel is true
/// 3. Workflow complete: IsWorkflowComplete is true, no next action
/// 4. Rejection: RejectedToParticipantId is set (workflow returns to sender)
/// </remarks>
public class RoutingResult
{
    /// <summary>
    /// The ID of the next action to perform.
    /// </summary>
    /// <remarks>
    /// Null if the workflow is complete or rejected.
    /// This ID corresponds to an action in the blueprint.
    /// For backward compatibility, also populated when a single-route result is created.
    /// </remarks>
    public string? NextActionId { get; set; }

    /// <summary>
    /// The participant ID (DID) who should perform the next action.
    /// </summary>
    /// <remarks>
    /// Null if the workflow is complete.
    /// Determined by evaluating routing conditions.
    /// </remarks>
    public string? NextParticipantId { get; set; }

    /// <summary>
    /// Indicates whether the workflow has reached completion.
    /// </summary>
    /// <remarks>
    /// True when no routing conditions match, indicating
    /// this is a terminal action with no further steps.
    /// </remarks>
    public bool IsWorkflowComplete { get; set; }

    /// <summary>
    /// If set, indicates the action was rejected and should return to this participant.
    /// </summary>
    /// <remarks>
    /// Used in approval workflows where an action can be rejected
    /// and sent back to the original requester.
    ///
    /// When set, this takes precedence over NextActionId/NextParticipantId.
    /// </remarks>
    public string? RejectedToParticipantId { get; set; }

    /// <summary>
    /// The routing condition that matched (for audit/debugging).
    /// </summary>
    /// <remarks>
    /// Contains the JSON Logic expression that evaluated to true.
    /// Useful for understanding why a particular route was chosen.
    /// </remarks>
    public string? MatchedCondition { get; set; }

    /// <summary>
    /// The id of the <see cref="Sorcha.Blueprint.Models.Route"/> that was taken. Null when no route
    /// matched (Feature 184).
    /// </summary>
    /// <remarks>
    /// Distinct from the per-next-action <see cref="RoutedAction.MatchedRouteId"/>, which is absent
    /// for a <b>terminal</b> route because such a route produces no next actions at all. A decision
    /// workflow's reject route is exactly that case, so the taken route's identity must also live at
    /// the top level. It rides the signed routing decision onto the transaction, letting any node
    /// that folds it locate the route in the replicated blueprint without re-evaluating conditions
    /// against a payload it may not be able to read.
    /// </remarks>
    public string? MatchedRouteId { get; set; }

    /// <summary>
    /// List of next actions for route-based routing, supporting parallel branches.
    /// </summary>
    /// <remarks>
    /// For single-route results, contains one entry matching NextActionId/NextParticipantId.
    /// For parallel results, contains multiple entries (one per branch).
    /// Empty for workflow-complete or rejection results.
    /// </remarks>
    public List<RoutedAction> NextActions { get; set; } = [];

    /// <summary>
    /// True when multiple next actions exist (parallel branch execution).
    /// </summary>
    public bool IsParallel { get; set; }

    /// <summary>
    /// Per-next-action prepopulated payloads derived from the matched route's
    /// <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/>. Null when the
    /// matched route declares no mapping. Key is the next action ID, value is
    /// the JSON object to seed into the instance's pending action payload store.
    /// </summary>
    /// <remarks>
    /// Transient carrier between <see cref="Sorcha.Blueprint.Engine.Interfaces.IRoutingEngine"/>
    /// and the action execution service. Not persisted on RoutingResult itself.
    /// Introduced in Feature 104 wave 14a.
    /// </remarks>
    public IReadOnlyDictionary<int, JsonObject>? PendingPayloads { get; set; }

    /// <summary>
    /// Creates a routing result for workflow completion.
    /// </summary>
    public static RoutingResult Complete() => new()
    {
        IsWorkflowComplete = true
    };

    /// <summary>
    /// Creates a routing result for the next action.
    /// </summary>
    /// <remarks>
    /// Populates both the singular NextActionId/NextParticipantId properties
    /// and the NextActions list for backward compatibility.
    /// </remarks>
    public static RoutingResult Next(string actionId, string participantId, string? matchedCondition = null) => new()
    {
        NextActionId = actionId,
        NextParticipantId = participantId,
        MatchedCondition = matchedCondition,
        NextActions = [new RoutedAction { ActionId = actionId, ParticipantId = participantId }]
    };

    /// <summary>
    /// Creates a routing result for parallel branch execution.
    /// </summary>
    /// <param name="actions">The list of routed actions to execute in parallel.</param>
    /// <param name="matchedCondition">The condition that produced this parallel result.</param>
    public static RoutingResult Parallel(List<RoutedAction> actions, string? matchedCondition = null) => new()
    {
        NextActions = actions ?? [],
        IsParallel = (actions?.Count ?? 0) > 1,
        MatchedCondition = matchedCondition,
        // For backward compatibility, set singular properties from first action
        NextActionId = actions?.FirstOrDefault()?.ActionId,
        NextParticipantId = actions?.FirstOrDefault()?.ParticipantId
    };

    /// <summary>
    /// Creates a routing result for rejection.
    /// </summary>
    public static RoutingResult Reject(string participantId) => new()
    {
        RejectedToParticipantId = participantId
    };
}
