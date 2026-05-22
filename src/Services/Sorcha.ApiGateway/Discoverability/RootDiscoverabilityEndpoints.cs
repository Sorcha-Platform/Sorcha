// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Serves the root-level AI-discoverability surface (spec 117 follow-up) that AI crawlers and
/// the llms.txt convention probe at the domain root:
/// <list type="bullet">
///   <item><c>GET /llms.txt</c> — the canonical machine-readable platform description.</item>
///   <item><c>GET /llms-full.txt</c> — the extended version.</item>
///   <item><c>GET /robots.txt</c> — explicitly welcomes AI crawlers, points to the sitemap + llms.txt.</item>
///   <item><c>GET /sitemap.xml</c> — the public entry points + the served well-known artefacts.</item>
/// </list>
/// <para>
/// <c>llms.txt</c> / <c>llms-full.txt</c> are the canonical authored files at the repo root and
/// <c>docs/</c>; they are embedded into this assembly (see the project's <c>EmbeddedResource</c>
/// items) so the served bytes are identical to the source of truth. <c>robots.txt</c> and
/// <c>sitemap.xml</c> are generated per-request from the request scheme + host so they are correct
/// on every domain the gateway fronts (n1.sorcha.dev, docs.sorcha.io, …) without configuration.
/// </para>
/// All responses are anonymous and cacheable. CORS is open via the gateway's <c>AddSorchaCors()</c>.
/// </summary>
internal static class RootDiscoverabilityEndpoints
{
    private const string CacheControl = "public, max-age=300";
    private const string LlmsResource = "Sorcha.ApiGateway.Discoverability.Content.llms.txt";
    private const string LlmsFullResource = "Sorcha.ApiGateway.Discoverability.Content.llms-full.txt";

    // AI crawler / agent user-agents we explicitly welcome. Listing them (Allow: /) is what gets
    // Sorcha into the assistants' indexes — the opposite of the common "block the bots" posture.
    private static readonly string[] AiUserAgents =
    {
        "GPTBot", "OAI-SearchBot", "ChatGPT-User", "ClaudeBot", "anthropic-ai", "Claude-Web",
        "PerplexityBot", "Perplexity-User", "Google-Extended", "CCBot", "cohere-ai", "Applebot-Extended"
    };

    public static IEndpointRouteBuilder MapRootDiscoverabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/llms.txt", new[] { "GET", "HEAD" },
            (HttpContext ctx) => WriteEmbeddedAsync(ctx, LlmsResource)).ExcludeFromDescription();

        endpoints.MapMethods("/llms-full.txt", new[] { "GET", "HEAD" },
            (HttpContext ctx) => WriteEmbeddedAsync(ctx, LlmsFullResource)).ExcludeFromDescription();

        endpoints.MapMethods("/robots.txt", new[] { "GET", "HEAD" }, WriteRobotsAsync)
            .ExcludeFromDescription();

        endpoints.MapMethods("/sitemap.xml", new[] { "GET", "HEAD" }, WriteSitemapAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task WriteEmbeddedAsync(HttpContext context, string resourceName)
    {
        var asm = typeof(RootDiscoverabilityEndpoints).Assembly;
        await using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);

        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = CacheControl;
        await context.Response.WriteAsync(body, Encoding.UTF8, context.RequestAborted);
    }

    private static async Task WriteRobotsAsync(HttpContext context)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var sb = new StringBuilder();
        sb.AppendLine("# Sorcha — AI agents and crawlers are welcome.");
        sb.AppendLine($"# Machine-readable platform description: {baseUrl}/llms.txt");
        sb.AppendLine();
        foreach (var ua in AiUserAgents)
        {
            sb.AppendLine($"User-agent: {ua}");
            sb.AppendLine("Allow: /");
            sb.AppendLine();
        }
        sb.AppendLine("User-agent: *");
        sb.AppendLine("Allow: /");
        sb.AppendLine("Disallow: /api/internal/");
        sb.AppendLine();
        sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");

        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = CacheControl;
        await context.Response.WriteAsync(sb.ToString(), Encoding.UTF8, context.RequestAborted);
    }

    private static async Task WriteSitemapAsync(HttpContext context)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        // Public entry points + the served machine-readable artefacts an AI agent should find.
        string[] paths =
        {
            "/", "/llms.txt", "/llms-full.txt",
            "/.well-known/openapi.json", "/.well-known/openapi.yaml", "/.well-known/mcp.json",
            "/app", "/verify", "/wallet"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var p in paths)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseUrl}{p}</loc>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");

        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers.CacheControl = CacheControl;
        await context.Response.WriteAsync(sb.ToString(), Encoding.UTF8, context.RequestAborted);
    }
}
