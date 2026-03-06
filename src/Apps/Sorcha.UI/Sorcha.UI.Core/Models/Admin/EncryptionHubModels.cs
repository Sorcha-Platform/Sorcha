// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// SignalR push update for encryption progress.
/// </summary>
public record EncryptionProgressUpdate
{
    public string OperationId { get; init; } = string.Empty;
    public int Step { get; init; }
    public string StepName { get; init; } = string.Empty;
    public int TotalSteps { get; init; }
    public int PercentComplete { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// SignalR push update for encryption completion.
/// </summary>
public record EncryptionCompleteUpdate
{
    public string OperationId { get; init; } = string.Empty;
    public string TransactionHash { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// SignalR push update for encryption failure.
/// </summary>
public record EncryptionFailedUpdate
{
    public string OperationId { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string? FailedRecipient { get; init; }
    public int? Step { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
