// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ApiGateway.Services;
using YamlDotNet.Serialization;

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Maps the well-known OpenAPI surface required by spec 117 FR-001 and FR-002:
/// <c>GET /.well-known/openapi.json</c> and <c>GET /.well-known/openapi.yaml</c>.
///
/// The handlers serve the <em>aggregated</em> OpenAPI document — i.e. the gateway's own
/// routes plus every backend service's routes via <see cref="OpenApiAggregationService"/>.
/// Per spec 117 design intent, an AI agent fetching one well-known URL must see the full
/// platform surface, not only the gateway's direct routes.
///
/// Both responses set <c>Cache-Control: public, max-age=300</c> per NFR-006 and are
/// anonymous per US1 acceptance scenarios. CORS is open via the gateway's <c>AddSorchaCors()</c>.
///
/// Discoverability extensions (<c>info.x-mcp-server</c>, <c>info.x-standards</c>) are injected
/// inline here because the aggregation service produces a <see cref="JsonObject"/> rather than
/// a <see cref="Microsoft.OpenApi.OpenApiDocument"/>, so the standard <c>IOpenApiDocumentTransformer</c>
/// pipeline does not apply.
/// </summary>
internal static class WellKnownOpenApiEndpoints
{
    private const string CacheControlValue = "public, max-age=300";
    private const string McpServerUrlKey = "Discoverability:McpServerUrl";
    private const string StandardsKey = "Discoverability:Standards";

    /// <summary>
    /// Wires both well-known routes. Call after the OpenAPI aggregation route has been
    /// registered so the underlying service is in DI.
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
        var doc = await BuildAggregatedDocumentAsync(context);

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = CacheControlValue;
        await context.Response.WriteAsync(
            doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            context.RequestAborted);
    }

    private static async Task HandleYamlAsync(HttpContext context)
    {
        var doc = await BuildAggregatedDocumentAsync(context);

        var graph = ConvertJsonNode(doc);
        var serializer = new SerializerBuilder().Build();
        var yaml = serializer.Serialize(graph);

        context.Response.ContentType = "application/yaml";
        context.Response.Headers.CacheControl = CacheControlValue;
        await context.Response.WriteAsync(yaml, Encoding.UTF8, context.RequestAborted);
    }

    private static async Task<JsonObject> BuildAggregatedDocumentAsync(HttpContext context)
    {
        var aggregator = context.RequestServices.GetRequiredService<OpenApiAggregationService>();
        var doc = await aggregator.GetAggregatedOpenApiAsync(context.RequestAborted);

        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        InjectDiscoverabilityExtensions(doc, configuration);

        return doc;
    }

    private static void InjectDiscoverabilityExtensions(JsonObject document, IConfiguration configuration)
    {
        if (document["info"] is not JsonObject info)
        {
            info = new JsonObject();
            document["info"] = info;
        }

        var mcpServerUrl = configuration[McpServerUrlKey];
        if (!string.IsNullOrWhiteSpace(mcpServerUrl))
        {
            info["x-mcp-server"] = mcpServerUrl;
        }

        var standards = configuration.GetSection(StandardsKey).Get<string[]>() ?? Array.Empty<string>();
        if (standards.Length > 0)
        {
            var arr = new JsonArray();
            foreach (var s in standards)
            {
                arr.Add(s);
            }
            info["x-standards"] = arr;
        }
    }

    internal static object? ConvertJsonNode(JsonNode? node) => node switch
    {
        JsonObject obj => obj.ToDictionary(p => p.Key, p => ConvertJsonNode(p.Value)),
        JsonArray arr => arr.Select(ConvertJsonNode).ToList(),
        JsonValue val => ConvertJsonValue(val),
        _ => null
    };

    internal static object? ConvertJsonValue(JsonValue value)
    {
        // JsonValue may be backed either by a JsonElement (when deserialised from JSON text)
        // or by a CLR primitive (when built in code via JsonArray.Add(string) or similar).
        // The OpenApiInfoTransformer populates info.x-standards with `arr.Add(string)`, so
        // the resulting JsonValue is string-backed, not JsonElement-backed. Try CLR types
        // first, fall back to JsonElement, then to raw JSON text.
        if (value.TryGetValue<string>(out var s)) return s;
        if (value.TryGetValue<bool>(out var b)) return b;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<int>(out var i)) return (long)i;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var ji) ? ji : (object)element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }
        return value.ToJsonString();
    }
}
