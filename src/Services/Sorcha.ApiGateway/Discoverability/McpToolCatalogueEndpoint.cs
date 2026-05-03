// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Maps <c>GET /api/mcp/tools</c> — the flat tool catalogue referenced by
/// <c>McpManifest.tool_catalogue_url</c>. Returns one descriptor per tool with name, category,
/// and the description-attribute text.
/// </summary>
internal static class McpToolCatalogueEndpoint
{
    private const string CacheControlValue = "public, max-age=300";

    public static IEndpointRouteBuilder MapMcpToolCatalogueEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mcp/tools", HandleAsync)
            .ExcludeFromDescription();
        return endpoints;
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var catalogue = context.RequestServices.GetRequiredService<ToolCatalogueProvider>();
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = CacheControlValue;
        await context.Response.WriteAsJsonAsync(catalogue.GetTools(), cancellationToken: context.RequestAborted);
    }
}
