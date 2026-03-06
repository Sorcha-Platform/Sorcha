// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// Represents a completed or failed encryption operation in the history list.
/// </summary>
public record OperationHistoryItem
{
    public string OperationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string BlueprintId { get; init; } = string.Empty;
    public string ActionTitle { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string WalletAddress { get; init; } = string.Empty;
    public int RecipientCount { get; init; }
    public string? TransactionHash { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}

/// <summary>
/// Paginated response for operations history listing.
/// </summary>
public record OperationHistoryPage
{
    public List<OperationHistoryItem> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public bool HasMore { get; init; }
}
