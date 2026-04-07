// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Agent.Models;

/// <summary>
/// Represents a pending action awaiting a decision from the actor.
/// </summary>
public record PendingAction
{
    public required string ActionId { get; init; }
    public required string ActionName { get; init; }
    public uint ActionIndex { get; init; }
    public required string BlueprintId { get; init; }
    public required string InstanceId { get; init; }
    public required string RegisterId { get; init; }
    public required string TransactionId { get; init; }
    public JsonElement? PreviousPayload { get; init; }
    public JsonElement? Schema { get; init; }
    public string? SenderAddress { get; init; }
}
