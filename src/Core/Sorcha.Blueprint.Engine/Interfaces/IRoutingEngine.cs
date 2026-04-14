// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Interfaces;

/// <summary>
/// Routing engine that determines the next action and participant in a workflow.
/// </summary>
/// <remarks>
/// Uses conditional routing based on JSON Logic expressions to determine
/// which participant should perform the next action.
/// 
/// Routing flow:
/// 1. Evaluate the current action's routing conditions in order
/// 2. First condition that evaluates to true determines the next participant
/// 3. Find the next action for that participant in the blueprint
/// 4. Return routing decision
/// 
/// Special cases:
/// - If no conditions match, the workflow is complete (terminal action)
/// - If a condition matches but no action exists for that participant, error
/// - If the next action is the same as current, it's a loop (usually an error)
/// 
/// Example routing conditions:
/// [
///   { "participantId": "manager", "condition": { "&gt;": [{"var": "amount"}, 10000] } },
///   { "participantId": "clerk", "condition": true }  // default/fallback
/// ]
/// </remarks>
public interface IRoutingEngine
{
    /// <summary>
    /// Determine the next action and participant based on routing conditions.
    /// </summary>
    /// <param name="blueprint">The blueprint definition containing all actions.</param>
    /// <param name="currentAction">The action that was just completed.</param>
    /// <param name="data">The action data used to evaluate conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A routing result containing:
    /// - The next action to perform (or null if workflow is complete)
    /// - The participant who should perform it
    /// - The condition that matched (for audit purposes)
    /// </returns>
    /// <remarks>
    /// This method evaluates routing conditions using the IJsonLogicEvaluator.
    /// Conditions are evaluated in the order they appear in the action definition.
    /// 
    /// The routing result can indicate:
    /// - Success: Next action and participant determined
    /// - Complete: No conditions matched (workflow finished)
    /// - Error: Condition matched but next action not found
    /// </remarks>
    Task<RoutingResult> DetermineNextAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action currentAction,
        Dictionary<string, object> data,
        CancellationToken ct = default);

    /// <summary>
    /// Determine the next action(s) and additionally evaluate Route.OutputMapping
    /// entries against an output source document, populating RoutingResult.PendingPayloads
    /// with prepopulated payloads for each next action.
    /// </summary>
    /// <param name="blueprint">The blueprint definition containing all actions.</param>
    /// <param name="currentAction">The action that was just completed.</param>
    /// <param name="data">The action data used to evaluate routing conditions.</param>
    /// <param name="outputSource">
    /// Source document for OutputMapping evaluation. Expected top-level keys are
    /// <c>payload</c>, <c>calculations</c>, and optionally <c>haip</c>. JSON Pointers
    /// in Route.OutputMapping are evaluated against this document. May be null,
    /// in which case no payload carry-forward is performed (backward compatible).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Introduced in Feature 104 wave 14a. When <paramref name="outputSource"/> is
    /// null, this method behaves identically to <see cref="DetermineNextAsync"/>.
    /// Absent source paths are silently skipped (not an error).
    /// </remarks>
    Task<RoutingResult> DetermineNextWithMappingAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action currentAction,
        Dictionary<string, object> data,
        JsonObject? outputSource,
        CancellationToken ct = default);
}
