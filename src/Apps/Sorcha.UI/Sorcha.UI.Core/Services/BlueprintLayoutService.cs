// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Blazor.Diagrams.Core.Geometry;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Designer;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Computes node positions for auto-layout of a blueprint's action graph
/// using a simplified Sugiyama (layered) algorithm.
/// </summary>
public class BlueprintLayoutService
{
    private const double NodeWidth = 280;
    private const double VerticalSpacing = 220;
    private const double HorizontalSpacing = 360;
    private const double StartXOffset = 80;
    private const double StartYOffset = 80;
    private const double SwimlaneHeaderHeight = 40;

    /// <summary>
    /// Computes a positioned layout for the given blueprint.
    /// </summary>
    public DiagramLayout ComputeLayout(Blueprint.Models.Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);

        if (blueprint.Actions is null || blueprint.Actions.Count == 0)
        {
            return new DiagramLayout([], [], [], 0, 0);
        }

        var actions = blueprint.Actions;
        var participants = blueprint.Participants ?? [];

        // Build participant legend
        var legend = participants.Select((p, i) => new ParticipantInfo(
            p.Id, p.Name, ParticipantInfo.GetColourForIndex(i))).ToList();

        // Build adjacency graph and collect edges
        var adjacency = BuildAdjacencyGraph(actions, out var edges);

        // Assign layers via BFS from starting actions
        var layers = AssignLayers(actions, adjacency);

        // Detect back-edges (target layer <= source layer)
        MarkBackEdges(edges, layers);

        // Identify cycle targets
        var cycleTargets = new HashSet<int>(edges.Where(e => e.IsBackEdge).Select(e => e.TargetActionId));

        // Build swimlane column mapping: participant sender → column index
        var swimlaneColumns = BuildSwimlaneColumns(actions, participants);

        // Order nodes within each layer
        var layerGroups = actions
            .GroupBy(a => layers.GetValueOrDefault(a.Id, 0))
            .OrderBy(g => g.Key)
            .ToList();

        // Compute positions using swimlane columns
        var nodes = new List<DiagramNode>();
        double maxX = 0;

        foreach (var group in layerGroups)
        {
            var layer = group.Key;
            var actionsInLayer = group.OrderBy(a => swimlaneColumns.GetValueOrDefault(a.Sender ?? "", 0)).ToList();

            for (int i = 0; i < actionsInLayer.Count; i++)
            {
                var action = actionsInLayer[i];
                var column = swimlaneColumns.GetValueOrDefault(action.Sender ?? "", i);
                double x = StartXOffset + column * HorizontalSpacing;
                double y = StartYOffset + SwimlaneHeaderHeight + layer * VerticalSpacing;

                bool isTerminal = IsTerminalAction(action);
                string summary = BuildDetailSummary(action);

                nodes.Add(new DiagramNode(
                    ActionId: action.Id,
                    Title: action.Title,
                    SenderParticipantId: action.Sender ?? string.Empty,
                    Layer: layer,
                    Position: new Point(x, y),
                    IsStarting: action.IsStartingAction,
                    IsTerminal: isTerminal,
                    IsCycleTarget: cycleTargets.Contains(action.Id),
                    DetailSummary: summary,
                    SwimlaneColumn: column));

                if (x + NodeWidth > maxX) maxX = x + NodeWidth;
            }
        }

        double totalHeight = layerGroups.Count > 0
            ? StartYOffset + layerGroups.Max(g => g.Key) * VerticalSpacing + VerticalSpacing
            : 0;
        double totalWidth = maxX + StartXOffset;

        return new DiagramLayout(nodes, edges, legend, totalWidth, totalHeight);
    }

    /// <summary>
    /// Builds a mapping of participant sender ID → swimlane column index.
    /// Column order follows participant list order.
    /// </summary>
    private static Dictionary<string, int> BuildSwimlaneColumns(
        List<BlueprintAction> actions, List<Participant> participants)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Use participant list order for columns
        for (int i = 0; i < participants.Count; i++)
        {
            // Map both ID and Name as potential sender values
            if (!string.IsNullOrEmpty(participants[i].Id))
                columns.TryAdd(participants[i].Id, i);
            if (!string.IsNullOrEmpty(participants[i].Name))
                columns.TryAdd(participants[i].Name, i);
        }

        // Assign columns for senders not in participant list
        int nextColumn = participants.Count;
        foreach (var action in actions)
        {
            if (!string.IsNullOrEmpty(action.Sender) && !columns.ContainsKey(action.Sender))
            {
                columns[action.Sender] = nextColumn++;
            }
        }

        return columns;
    }

    private static Dictionary<int, List<int>> BuildAdjacencyGraph(
        List<BlueprintAction> actions, out List<DiagramEdge> edges)
    {
        var adjacency = new Dictionary<int, List<int>>();
        edges = [];

        foreach (var action in actions)
        {
            if (!adjacency.ContainsKey(action.Id))
                adjacency[action.Id] = [];

            // Modern routing via Routes
            if (action.Routes is not null && action.Routes.Any())
            {
                foreach (var route in action.Routes)
                {
                    var nextIds = route.NextActionIds?.ToList() ?? [];

                    if (nextIds.Count == 0)
                    {
                        // Terminal route — workflow ends
                        edges.Add(new DiagramEdge(
                            SourceActionId: action.Id,
                            TargetActionId: -1,
                            RouteId: route.Id,
                            EdgeType: EdgeType.Terminal,
                            Label: route.Description,
                            IsBackEdge: false));
                        continue;
                    }

                    var edgeType = route.Condition is not null && !route.IsDefault
                        ? EdgeType.Conditional
                        : EdgeType.Default;

                    foreach (var nextId in nextIds)
                    {
                        adjacency[action.Id].Add(nextId);
                        edges.Add(new DiagramEdge(
                            SourceActionId: action.Id,
                            TargetActionId: nextId,
                            RouteId: route.Id,
                            EdgeType: edgeType,
                            Label: route.Description,
                            IsBackEdge: false));
                    }
                }
            }
            // Legacy routing: Condition has Principal/Criteria only (no nextActionId).
            // In legacy mode, actions are implicitly sequential by Id order.
            else if (action.Participants is not null && action.Participants.Any())
            {
                var nextAction = actions.FirstOrDefault(a => a.Id == action.Id + 1);
                if (nextAction is not null)
                {
                    adjacency[action.Id].Add(nextAction.Id);
                    edges.Add(new DiagramEdge(
                        SourceActionId: action.Id,
                        TargetActionId: nextAction.Id,
                        RouteId: null,
                        EdgeType: EdgeType.Default,
                        Label: null,
                        IsBackEdge: false));
                }
            }

            // Rejection routing
            if (action.RejectionConfig is not null)
            {
                var rejectActionId = action.RejectionConfig.TargetActionId;
                adjacency[action.Id].Add(rejectActionId);
                edges.Add(new DiagramEdge(
                    SourceActionId: action.Id,
                    TargetActionId: rejectActionId,
                    RouteId: null,
                    EdgeType: EdgeType.Rejection,
                    Label: "Reject",
                    IsBackEdge: false));
            }
        }

        return adjacency;
    }

    private static Dictionary<int, int> AssignLayers(
        List<BlueprintAction> actions, Dictionary<int, List<int>> adjacency)
    {
        var layers = new Dictionary<int, int>();
        var visited = new HashSet<int>();

        // Start BFS from starting actions
        var startingActions = actions.Where(a => a.IsStartingAction).ToList();
        if (startingActions.Count == 0 && actions.Count > 0)
        {
            // Fallback: use first action
            startingActions = [actions[0]];
        }

        var queue = new Queue<(int actionId, int depth)>();
        foreach (var start in startingActions)
        {
            queue.Enqueue((start.Id, 0));
            layers[start.Id] = 0;
        }

        while (queue.Count > 0)
        {
            var (actionId, depth) = queue.Dequeue();

            if (!visited.Add(actionId))
                continue;

            if (!adjacency.TryGetValue(actionId, out var neighbors))
                continue;

            foreach (var neighbor in neighbors)
            {
                if (neighbor < 0) continue; // Skip terminal markers

                var nextDepth = depth + 1;
                if (!layers.ContainsKey(neighbor) || layers[neighbor] < nextDepth)
                {
                    // Only deepen if not already visited (avoids cycle issues)
                    if (!visited.Contains(neighbor))
                    {
                        layers[neighbor] = nextDepth;
                        queue.Enqueue((neighbor, nextDepth));
                    }
                }
            }
        }

        // Assign unreachable actions to layer 0
        foreach (var action in actions)
        {
            layers.TryAdd(action.Id, 0);
        }

        return layers;
    }

    private static void MarkBackEdges(List<DiagramEdge> edges, Dictionary<int, int> layers)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge.TargetActionId < 0) continue; // Terminal edges

            var sourceLayer = layers.GetValueOrDefault(edge.SourceActionId, 0);
            var targetLayer = layers.GetValueOrDefault(edge.TargetActionId, 0);

            if (targetLayer <= sourceLayer && edge.EdgeType != EdgeType.Terminal)
            {
                edges[i] = edge with { IsBackEdge = true, EdgeType = EdgeType.BackEdge, Label = edge.Label ?? "loop" };
            }
        }
    }

    private static int GetParentOrder(int actionId, Dictionary<int, List<int>> adjacency, Dictionary<int, int> layers)
    {
        // Find parent (action whose adjacency contains this actionId)
        // Use parent's position within its layer for ordering
        var actionLayer = layers.GetValueOrDefault(actionId, 0);

        foreach (var (parentId, neighbors) in adjacency)
        {
            if (neighbors.Contains(actionId) && layers.GetValueOrDefault(parentId, 0) < actionLayer)
            {
                return parentId;
            }
        }

        return actionId; // No parent — use self
    }

    private static bool IsTerminalAction(BlueprintAction action)
    {
        if (action.Routes is not null && action.Routes.Any())
        {
            // Terminal if all routes have empty nextActionIds
            return action.Routes.All(r => r.NextActionIds is null || !r.NextActionIds.Any());
        }

        // Legacy: terminal if no participants routing
        if (action.Participants is not null && action.Participants.Any())
            return false;

        return true;
    }

    private static string BuildDetailSummary(BlueprintAction action)
    {
        var parts = new List<string>();

        var schemaCount = action.DataSchemas?.Count() ?? 0;
        if (schemaCount > 0)
            parts.Add($"{schemaCount} schema{(schemaCount > 1 ? "s" : "")}");

        var disclosureCount = action.Disclosures?.Count() ?? 0;
        if (disclosureCount > 0)
            parts.Add($"{disclosureCount} disclosure{(disclosureCount > 1 ? "s" : "")}");

        var routeCount = action.Routes?.Count() ?? 0;
        if (routeCount > 0)
            parts.Add($"{routeCount} route{(routeCount > 1 ? "s" : "")}");

        return parts.Count > 0 ? string.Join(", ", parts) : "No details";
    }
}
