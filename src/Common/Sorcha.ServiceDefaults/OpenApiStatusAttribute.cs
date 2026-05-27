// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Marks an endpoint with an <c>x-status</c> OpenAPI extension. Spec 117 (AI Discoverability)
/// FR-010 — endpoints whose specification is incomplete or whose behaviour is not yet stable
/// MUST carry <c>x-status: "partial"</c> rather than be omitted from the OpenAPI document.
///
/// Apply via <c>.WithMetadata(new OpenApiStatusAttribute("partial"))</c> on an endpoint
/// registration. The matching <c>OpenApiStatusOperationTransformer</c> registered by
/// <c>AddSorchaOpenApi</c> reads the metadata at OpenAPI document generation time and
/// injects the extension onto the served operation.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OpenApiStatusAttribute(string status) : Attribute
{
    /// <summary>The status value injected as <c>x-status</c>. Conventionally <c>"partial"</c>, <c>"deprecated"</c>, or <c>"experimental"</c>.</summary>
    public string Status { get; } = status;
}
