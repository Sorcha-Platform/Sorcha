// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Computes DAG layout positions and manages highlight state for transaction graph visualization.
/// </summary>
public interface IGraphLayoutService
{
    /// <summary>
    /// Compute layout positions for all nodes and build edges.
    /// </summary>
    (List<TransactionGraphNode> Nodes, List<TransactionGraphEdge> Edges) ComputeLayout(
        IReadOnlyList<TransactionGraphNode> nodes, bool leftToRight = true);

    /// <summary>
    /// Highlight the chain from a selected node back to genesis.
    /// </summary>
    void HighlightChain(
        IReadOnlyList<TransactionGraphNode> nodes,
        IReadOnlyList<TransactionGraphEdge> edges,
        string selectedTxId);

    /// <summary>
    /// Clear all highlights.
    /// </summary>
    void ClearHighlights(
        IReadOnlyList<TransactionGraphNode> nodes,
        IReadOnlyList<TransactionGraphEdge> edges);
}

/// <summary>
/// Stateless DAG layout engine using BFS rank assignment and grid-based positioning.
/// </summary>
public class GraphLayoutService : IGraphLayoutService
{
    private const double RankSpacing = 180.0;
    private const double OrderSpacing = 80.0;

    /// <inheritdoc />
    public (List<TransactionGraphNode> Nodes, List<TransactionGraphEdge> Edges) ComputeLayout(
        IReadOnlyList<TransactionGraphNode> nodes, bool leftToRight = true)
    {
        if (nodes.Count == 0)
        {
            return ([], []);
        }

        // Build lookup and adjacency: parent TxId → list of child nodes
        var byTxId = new Dictionary<string, TransactionGraphNode>(nodes.Count);
        var children = new Dictionary<string, List<TransactionGraphNode>>();

        foreach (var node in nodes)
        {
            byTxId[node.TxId] = node;
        }

        foreach (var node in nodes)
        {
            if (!node.IsGenesis && byTxId.ContainsKey(node.PrevTxId))
            {
                if (!children.TryGetValue(node.PrevTxId, out var list))
                {
                    list = [];
                    children[node.PrevTxId] = list;
                }
                list.Add(node);
            }
        }

        // BFS from genesis nodes to assign ranks
        var visited = new HashSet<string>();
        var queue = new Queue<TransactionGraphNode>();

        foreach (var node in nodes.Where(n => n.IsGenesis).OrderBy(n => n.TimeStamp))
        {
            node.Rank = 0;
            visited.Add(node.TxId);
            queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (children.TryGetValue(current.TxId, out var childList))
            {
                foreach (var child in childList.OrderBy(c => c.TimeStamp))
                {
                    if (visited.Add(child.TxId))
                    {
                        child.Rank = current.Rank + 1;
                        queue.Enqueue(child);
                    }
                }
            }
        }

        // Assign ranks to orphan nodes (not reachable from any genesis)
        var orphans = nodes.Where(n => !visited.Contains(n.TxId)).OrderBy(n => n.TimeStamp).ToList();
        var maxRank = visited.Count > 0 ? nodes.Where(n => visited.Contains(n.TxId)).Max(n => n.Rank) : -1;

        foreach (var orphan in orphans)
        {
            orphan.Rank = ++maxRank;
        }

        // Group by rank and assign order within rank by timestamp
        var byRank = nodes.GroupBy(n => n.Rank).OrderBy(g => g.Key);

        foreach (var rankGroup in byRank)
        {
            var ordered = rankGroup.OrderBy(n => n.TimeStamp).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].OrderInRank = i;
            }
        }

        // Compute coordinates
        foreach (var node in nodes)
        {
            if (leftToRight)
            {
                node.X = node.Rank * RankSpacing;
                node.Y = node.OrderInRank * OrderSpacing;
            }
            else
            {
                node.X = node.OrderInRank * RankSpacing;
                node.Y = node.Rank * OrderSpacing;
            }
        }

        // Build edges: for each non-genesis node whose parent is in the set, create an edge
        var edges = new List<TransactionGraphEdge>();

        foreach (var node in nodes)
        {
            if (!node.IsGenesis && byTxId.ContainsKey(node.PrevTxId))
            {
                edges.Add(new TransactionGraphEdge
                {
                    SourceTxId = node.PrevTxId,
                    TargetTxId = node.TxId
                });
            }
        }

        return (nodes.ToList(), edges);
    }

    /// <inheritdoc />
    public void HighlightChain(
        IReadOnlyList<TransactionGraphNode> nodes,
        IReadOnlyList<TransactionGraphEdge> edges,
        string selectedTxId)
    {
        ClearHighlights(nodes, edges);

        var byTxId = nodes.ToDictionary(n => n.TxId);
        var edgeLookup = edges.ToDictionary(e => (e.SourceTxId, e.TargetTxId));

        var currentId = selectedTxId;

        while (currentId != null && byTxId.TryGetValue(currentId, out var node))
        {
            node.IsHighlighted = true;

            if (node.IsGenesis)
            {
                break;
            }

            // Highlight the edge from parent to this node
            if (edgeLookup.TryGetValue((node.PrevTxId, node.TxId), out var edge))
            {
                edge.IsHighlighted = true;
            }

            currentId = node.PrevTxId;
        }
    }

    /// <inheritdoc />
    public void ClearHighlights(
        IReadOnlyList<TransactionGraphNode> nodes,
        IReadOnlyList<TransactionGraphEdge> edges)
    {
        foreach (var node in nodes)
        {
            node.IsHighlighted = false;
        }

        foreach (var edge in edges)
        {
            edge.IsHighlighted = false;
        }
    }
}
