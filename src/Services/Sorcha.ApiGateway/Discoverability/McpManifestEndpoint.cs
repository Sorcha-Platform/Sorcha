// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sorcha.ApiGateway.Discoverability.Models;

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Maps <c>GET /.well-known/mcp.json</c> (spec 117 FR-012). Anonymous, cacheable, served as
/// <c>application/json</c> with <c>Cache-Control: public, max-age=300</c> per NFR-006.
///
/// The manifest is assembled once at handler-invocation time from <see cref="McpManifestOptions"/>
/// plus the live tool counts from <see cref="ToolCatalogueProvider"/> and the gateway's
/// assembly informational version (FR-046 — same source as the OpenAPI document's
/// <c>info.version</c>).
/// </summary>
internal static class McpManifestEndpoint
{
    private const string CacheControlValue = "public, max-age=300";
    private const string ManifestSchemaUrl =
        "https://github.com/Sorcha-Platform/Sorcha/blob/master/specs/117-ai-discoverability/contracts/mcp-manifest.schema.json";

    public static IEndpointRouteBuilder MapMcpManifestEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/mcp.json", HandleAsync)
            .ExcludeFromDescription();
        return endpoints;
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<McpManifestOptions>>().Value;
        var catalogue = context.RequestServices.GetRequiredService<ToolCatalogueProvider>();

        var counts = catalogue.GetCategoryCounts();
        var version = ResolveAssemblyInformationalVersion();

        var manifest = new McpManifest(
            Schema: ManifestSchemaUrl,
            Name: options.Name,
            Version: version,
            Description: options.Description,
            Transports: BuildTransports(options),
            Authentication: new McpAuthentication(
                Type: "jwt-bearer",
                Issuer: options.AuthIssuer,
                Audience: options.AuthAudience,
                AcquisitionUrl: options.AuthAcquisitionUrl),
            ToolCategories: new Dictionary<string, McpToolCategory>
            {
                ["admin"] = new(counts.Admin, options.AdminCategoryDescription),
                ["designer"] = new(counts.Designer, options.DesignerCategoryDescription),
                ["participant"] = new(counts.Participant, options.ParticipantCategoryDescription)
            },
            ToolCatalogueUrl: ResolveAbsoluteUrl(context, "/api/mcp/tools"),
            DocumentationUrl: options.DocumentationUrl);

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = CacheControlValue;
        await context.Response.WriteAsJsonAsync(manifest, cancellationToken: context.RequestAborted);
    }

    private static IReadOnlyList<McpTransport> BuildTransports(McpManifestOptions options)
    {
        var list = new List<McpTransport>(2)
        {
            new(
                Type: "stdio",
                Command: options.StdioCommand,
                Args: options.StdioArgs.ToArray(),
                Url: null)
        };

        if (!string.IsNullOrWhiteSpace(options.HttpSseUrl))
        {
            list.Add(new McpTransport(
                Type: "http+sse",
                Command: null,
                Args: null,
                Url: options.HttpSseUrl));
        }

        return list;
    }

    private static string ResolveAbsoluteUrl(HttpContext context, string path)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host}";
        return $"{origin}{path}";
    }

    private static string ResolveAssemblyInformationalVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var plusIdx = raw.IndexOf('+');
        return plusIdx > 0 ? raw[..plusIdx] : raw;
    }
}
