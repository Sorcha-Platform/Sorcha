// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ApiGateway.Discoverability.Models;

/// <summary>
/// One entry in the MCP manifest's <c>tool_categories</c> map. Spec 117 FR-016 — every category
/// (admin / designer / participant) carries a tool count and a one-sentence description of when
/// an agent should use that slice.
/// </summary>
public sealed record McpToolCategory(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("description")] string Description);
