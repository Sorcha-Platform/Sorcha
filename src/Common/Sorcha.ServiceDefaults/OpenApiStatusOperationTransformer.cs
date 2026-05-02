// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// OpenAPI operation transformer that reads <see cref="OpenApiStatusAttribute"/> metadata
/// from each endpoint and injects an <c>x-status</c> extension on the matching operation.
/// Spec 117 (AI Discoverability) FR-010 — exposes incomplete or unstable endpoints with
/// a discoverable status marker rather than omitting them.
/// </summary>
internal sealed class OpenApiStatusOperationTransformer : IOpenApiOperationTransformer
{
    private const string Extension = "x-status";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var statusAttr = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<OpenApiStatusAttribute>()
            .FirstOrDefault();

        if (statusAttr is null)
        {
            return Task.CompletedTask;
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[Extension] = new JsonNodeExtension(JsonValue.Create(statusAttr.Status)!);
        return Task.CompletedTask;
    }
}
