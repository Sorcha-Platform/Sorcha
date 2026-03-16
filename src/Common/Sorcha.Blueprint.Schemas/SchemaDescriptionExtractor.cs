// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Extracts property descriptions from JSON Schema documents for a given Control.Scope.
/// Used as the fallback help text source when Control.Instructions is not set.
/// Priority: explicit Control.Instructions > schema property description > nothing.
/// </summary>
public static class SchemaDescriptionExtractor
{
    /// <summary>
    /// Resolves the schema description for a control scope path.
    /// </summary>
    /// <param name="scope">The control scope (e.g., "/requestType", "/address/street")</param>
    /// <param name="dataSchemas">The action's DataSchemas collection</param>
    /// <returns>The schema property description, or null if not found</returns>
    public static string? GetDescription(string? scope, IEnumerable<JsonDocument>? dataSchemas)
    {
        if (string.IsNullOrWhiteSpace(scope) || dataSchemas is null)
            return null;

        // Normalize scope: strip leading "/" for path resolution
        var path = scope.TrimStart('/');

        foreach (var schema in dataSchemas)
        {
            var description = ResolvePropertyDescription(schema.RootElement, path);
            if (description is not null)
                return description;
        }

        return null;
    }

    /// <summary>
    /// Resolves the schema description for a control scope path from a single schema.
    /// </summary>
    /// <param name="scope">The control scope (e.g., "/requestType")</param>
    /// <param name="schema">A single JSON Schema document</param>
    /// <returns>The schema property description, or null if not found</returns>
    public static string? GetDescription(string? scope, JsonDocument? schema)
    {
        if (string.IsNullOrWhiteSpace(scope) || schema is null)
            return null;

        var path = scope.TrimStart('/');
        return ResolvePropertyDescription(schema.RootElement, path);
    }

    /// <summary>
    /// Resolves a property description by navigating the schema's properties tree.
    /// Supports nested paths like "address/street" → properties.address.properties.street.description.
    /// </summary>
    private static string? ResolvePropertyDescription(JsonElement schemaElement, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = schemaElement;

        foreach (var segment in segments)
        {
            // Navigate into "properties" object
            if (!current.TryGetProperty("properties", out var properties))
                return null;

            if (!properties.TryGetProperty(segment, out var property))
                return null;

            // Resolve $ref if present
            if (property.TryGetProperty("$ref", out var refElement) && refElement.ValueKind == JsonValueKind.String)
            {
                var resolved = ResolveRef(schemaElement, refElement.GetString()!);
                if (resolved is null)
                    return null;
                property = resolved.Value;
            }

            current = property;
        }

        // Extract description from the resolved property
        if (current.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String)
            return description.GetString();

        return null;
    }

    /// <summary>
    /// Resolves a JSON Schema $ref to the target element within the same document.
    /// Supports #/$defs/Name and #/definitions/Name patterns.
    /// </summary>
    private static JsonElement? ResolveRef(JsonElement root, string refPath)
    {
        if (!refPath.StartsWith("#/"))
            return null;

        var segments = refPath[2..].Split('/');
        var current = root;

        foreach (var segment in segments)
        {
            if (!current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }

        return current;
    }
}
