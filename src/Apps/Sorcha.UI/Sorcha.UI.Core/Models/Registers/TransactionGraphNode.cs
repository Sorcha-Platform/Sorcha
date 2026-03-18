// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// A node in the Register Map DAG visualization.
/// </summary>
public record TransactionGraphNode
{
    public required string TxId { get; init; }
    public required string PrevTxId { get; init; }
    public required string SenderWallet { get; init; }
    public DateTime TimeStamp { get; init; }
    public ulong? DocketNumber { get; init; }
    public string? BlueprintId { get; init; }
    public string? InstanceId { get; init; }
    public string TransactionType { get; init; } = "Unknown";

    // Layout coordinates (computed by GraphLayoutService)
    public double X { get; set; }
    public double Y { get; set; }

    // Computed properties
    public bool IsGenesis => string.IsNullOrEmpty(PrevTxId) || PrevTxId.All(c => c == '0');
    public bool IsHighlighted { get; set; }
    public int Rank { get; set; }
    public int OrderInRank { get; set; }

    public string TxIdTruncated => TxId.Length > 8 ? $"{TxId[..8]}..." : TxId;
    public string SenderTruncated => SenderWallet.Length > 12
        ? $"{SenderWallet[..6]}...{SenderWallet[^4..]}"
        : SenderWallet;
}

/// <summary>
/// A directed edge in the Register Map DAG.
/// </summary>
public record TransactionGraphEdge
{
    public required string SourceTxId { get; init; }
    public required string TargetTxId { get; init; }
    public bool IsHighlighted { get; set; }
}

/// <summary>
/// Response from the transaction graph API endpoint.
/// </summary>
public record TransactionGraphResponse
{
    public string RegisterId { get; init; } = "";
    public TransactionGraphNode[] Nodes { get; init; } = [];
    public int TotalCount { get; init; }
    public bool HasMore { get; init; }
}
