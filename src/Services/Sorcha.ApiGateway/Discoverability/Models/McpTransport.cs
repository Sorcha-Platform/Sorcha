// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ApiGateway.Discoverability.Models;

/// <summary>
/// One transport entry inside the MCP manifest's <c>transports[]</c> array.
/// Spec 117 FR-014 — at minimum <c>stdio</c> and <c>http+sse</c> must be listed with their
/// routing details (command/args for stdio, base URL for http+sse).
/// </summary>
public sealed record McpTransport(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("command"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Command,
    [property: JsonPropertyName("args"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Args,
    [property: JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url);
