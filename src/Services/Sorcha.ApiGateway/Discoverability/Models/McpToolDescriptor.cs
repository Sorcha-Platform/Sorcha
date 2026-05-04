// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ApiGateway.Discoverability.Models;

/// <summary>
/// Wire shape for one entry returned by the tool-catalogue endpoint
/// <c>GET /api/mcp/tools</c> referenced by <c>McpManifest.ToolCatalogueUrl</c>.
/// </summary>
public sealed record McpToolDescriptor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("description")] string Description);
