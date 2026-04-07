// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Models;

/// <summary>
/// The decision made by an actor for a pending action.
/// </summary>
public record ActionDecision(
    string Decision,
    Dictionary<string, object>? Payload,
    string? Reasoning,
    PreAction[]? PreActions = null);
