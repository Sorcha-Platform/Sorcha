// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Models;

/// <summary>
/// The decision made by an actor for a pending action.
/// </summary>
/// <param name="Decision">The decision type: "approve", "reject", or "skip".</param>
/// <param name="Payload">Optional payload data to include with the decision.</param>
/// <param name="Reasoning">Optional reasoning for the decision.</param>
public record ActionDecision(
    string Decision,
    Dictionary<string, object>? Payload,
    string? Reasoning);
