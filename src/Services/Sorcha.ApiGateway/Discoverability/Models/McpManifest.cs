// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ApiGateway.Discoverability.Models;

/// <summary>
/// Wire shape for <c>GET /.well-known/mcp.json</c>. Spec 117 FR-013 — required fields:
/// <c>name</c>, <c>version</c>, <c>description</c>, <c>transports</c>, <c>authentication</c>,
/// <c>tool_categories</c>, <c>tool_catalogue_url</c>, <c>documentation_url</c>.
/// </summary>
public sealed record McpManifest(
    [property: JsonPropertyName("$schema")] string? Schema,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("transports")] IReadOnlyList<McpTransport> Transports,
    [property: JsonPropertyName("authentication")] McpAuthentication Authentication,
    [property: JsonPropertyName("tool_categories")] IReadOnlyDictionary<string, McpToolCategory> ToolCategories,
    [property: JsonPropertyName("tool_catalogue_url")] string ToolCatalogueUrl,
    [property: JsonPropertyName("documentation_url")] string DocumentationUrl);
