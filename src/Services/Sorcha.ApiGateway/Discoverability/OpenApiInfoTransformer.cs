// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Document transformer that injects the AI-discoverability extensions onto the served OpenAPI
/// document's <c>info</c> block: <c>x-mcp-server</c> and <c>x-standards</c>.
/// Spec 117 FR-008 / FR-009. Per FR-046 the document version is sourced from the assembly
/// informational version by the upstream <c>AddSorchaOpenApi</c> transformer; this transformer
/// only adds the discoverability extensions on top.
/// </summary>
internal sealed class OpenApiInfoTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    private const string McpServerUrlKey = "Discoverability:McpServerUrl";
    private const string StandardsKey = "Discoverability:Standards";
    private const string McpServerExtension = "x-mcp-server";
    private const string StandardsExtension = "x-standards";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info.Extensions ??= new Dictionary<string, IOpenApiExtension>();

        var mcpServerUrl = configuration[McpServerUrlKey];
        if (!string.IsNullOrWhiteSpace(mcpServerUrl))
        {
            document.Info.Extensions[McpServerExtension] = new JsonNodeExtension(JsonValue.Create(mcpServerUrl)!);
        }

        var standards = configuration.GetSection(StandardsKey).Get<string[]>() ?? Array.Empty<string>();
        if (standards.Length > 0)
        {
            var arr = new JsonArray();
            foreach (var s in standards)
            {
                arr.Add(JsonValue.Create(s));
            }
            document.Info.Extensions[StandardsExtension] = new JsonNodeExtension(arr);
        }

        return Task.CompletedTask;
    }
}
