// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Maps the well-known OpenAPI surface required by spec 117 FR-001 and FR-002:
/// <c>GET /.well-known/openapi.json</c> (alias of the live document at <c>/openapi/v1.json</c>)
/// and <c>GET /.well-known/openapi.yaml</c> (the same document serialised as YAML).
/// Both responses set <c>Cache-Control: public, max-age=300</c> per NFR-006 and are anonymous
/// per US1 acceptance scenarios. CORS is open via the gateway's <c>AddSorchaCors()</c>.
/// </summary>
internal static class WellKnownOpenApiEndpoints
{
    private const string CacheControlValue = "public, max-age=300";
    private const string DefaultDocumentName = "v1";

    /// <summary>
    /// Wires both well-known routes. Call after <c>app.MapOpenApi()</c> so the underlying
    /// document provider is registered first.
    /// </summary>
    public static IEndpointRouteBuilder MapWellKnownOpenApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/openapi.json", HandleJsonAsync)
            .ExcludeFromDescription();

        endpoints.MapGet("/.well-known/openapi.yaml", HandleYamlAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task HandleJsonAsync(HttpContext context)
    {
        var document = await GetDocumentAsync(context);
        if (document is null)
        {
            return;
        }

        var sw = new StringWriter();
        var writer = new OpenApiJsonWriter(sw);
        document.SerializeAsV31(writer);

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = CacheControlValue;
        await context.Response.WriteAsync(sw.ToString(), Encoding.UTF8, context.RequestAborted);
    }

    private static async Task HandleYamlAsync(HttpContext context)
    {
        var document = await GetDocumentAsync(context);
        if (document is null)
        {
            return;
        }

        var sw = new StringWriter();
        var writer = new OpenApiYamlWriter(sw);
        document.SerializeAsV31(writer);

        context.Response.ContentType = "application/yaml";
        context.Response.Headers.CacheControl = CacheControlValue;
        await context.Response.WriteAsync(sw.ToString(), Encoding.UTF8, context.RequestAborted);
    }

    private static async Task<OpenApiDocument?> GetDocumentAsync(HttpContext context)
    {
        // The default OpenAPI document is registered under the key "v1" by AddOpenApi() with
        // no name argument. AddSorchaOpenApi calls AddOpenApi() with no name, so this is
        // correct for the gateway's setup.
        var provider = context.RequestServices.GetKeyedService<IOpenApiDocumentProvider>(DefaultDocumentName)
            ?? context.RequestServices.GetService<IOpenApiDocumentProvider>();

        if (provider is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync(
                "OpenAPI document provider is not registered.",
                context.RequestAborted);
            return null;
        }

        return await provider.GetOpenApiDocumentAsync(context.RequestAborted);
    }
}
