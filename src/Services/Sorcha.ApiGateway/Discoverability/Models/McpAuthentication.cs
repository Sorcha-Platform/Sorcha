// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ApiGateway.Discoverability.Models;

/// <summary>
/// MCP manifest <c>authentication</c> object. Spec 117 FR-015 — names <c>jwt-bearer</c>,
/// the JWT issuer URL, the audience, and a link to the JWT acquisition flow.
/// </summary>
public sealed record McpAuthentication(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("acquisition_url")] string AcquisitionUrl);
