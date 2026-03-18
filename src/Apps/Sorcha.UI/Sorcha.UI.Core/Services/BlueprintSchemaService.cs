// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Lightweight label/description pair extracted from a JSON Schema property
/// for annotating payload fields in the Transaction Explorer.
/// </summary>
public record SchemaOverlayFieldInfo(string Label, string? Description);

/// <summary>
/// Fetches blueprint action schemas and extracts field labels/descriptions
/// for annotating decoded payloads in the Transaction Explorer.
/// </summary>
public interface IBlueprintSchemaService
{
    /// <summary>
    /// Gets field label and description information for the specified blueprint action's schema.
    /// Returns null on any failure (HTTP error, parse error, missing data).
    /// </summary>
    Task<Dictionary<string, SchemaOverlayFieldInfo>?> GetActionSchemaFieldsAsync(
        string blueprintId, uint actionId, CancellationToken ct = default);
}

/// <summary>
/// Implementation of <see cref="IBlueprintSchemaService"/> that fetches blueprint definitions
/// from the Blueprint Service API and extracts JSON Schema property metadata.
/// </summary>
public class BlueprintSchemaService : IBlueprintSchemaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BlueprintSchemaService> _logger;

    /// <summary>
    /// In-memory cache keyed by "{blueprintId}:{actionId}".
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, SchemaOverlayFieldInfo>> _cache = new();

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintSchemaService"/>.
    /// </summary>
    public BlueprintSchemaService(HttpClient httpClient, ILogger<BlueprintSchemaService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, SchemaOverlayFieldInfo>?> GetActionSchemaFieldsAsync(
        string blueprintId, uint actionId, CancellationToken ct = default)
    {
        var cacheKey = $"{blueprintId}:{actionId}";

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var response = await _httpClient.GetAsync($"/api/blueprints/{blueprintId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Failed to fetch blueprint {BlueprintId}: {StatusCode}",
                    blueprintId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Navigate to the actions array
            if (!root.TryGetProperty("actions", out var actionsElement) ||
                actionsElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogDebug("Blueprint {BlueprintId} has no actions array", blueprintId);
                return null;
            }

            // Find the action with the matching ID
            JsonElement? matchingAction = null;
            foreach (var action in actionsElement.EnumerateArray())
            {
                if (action.TryGetProperty("id", out var idElement) &&
                    idElement.TryGetUInt32(out var id) && id == actionId)
                {
                    matchingAction = action;
                    break;
                }
            }

            if (matchingAction is null)
            {
                _logger.LogDebug("Action {ActionId} not found in blueprint {BlueprintId}",
                    actionId, blueprintId);
                return null;
            }

            // Extract fields from dataSchemas
            var fields = new Dictionary<string, SchemaOverlayFieldInfo>();

            if (matchingAction.Value.TryGetProperty("dataSchemas", out var dataSchemasElement) &&
                dataSchemasElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var schema in dataSchemasElement.EnumerateArray())
                {
                    ExtractFieldsFromSchema(schema, fields);
                }
            }

            if (fields.Count == 0)
            {
                _logger.LogDebug("No schema fields found for action {ActionId} in blueprint {BlueprintId}",
                    actionId, blueprintId);
                return null;
            }

            _cache[cacheKey] = fields;
            return fields;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error fetching schema for blueprint {BlueprintId} action {ActionId}",
                blueprintId, actionId);
            return null;
        }
    }

    /// <summary>
    /// Extracts property labels and descriptions from a JSON Schema's "properties" object.
    /// Uses "title" as the label and "description" as the description.
    /// </summary>
    private static void ExtractFieldsFromSchema(JsonElement schema, Dictionary<string, SchemaOverlayFieldInfo> fields)
    {
        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in properties.EnumerateObject())
        {
            // Skip if already extracted from a previous schema
            if (fields.ContainsKey(property.Name))
            {
                continue;
            }

            string? label = null;
            string? description = null;

            if (property.Value.TryGetProperty("title", out var titleElement) &&
                titleElement.ValueKind == JsonValueKind.String)
            {
                label = titleElement.GetString();
            }

            if (property.Value.TryGetProperty("description", out var descElement) &&
                descElement.ValueKind == JsonValueKind.String)
            {
                description = descElement.GetString();
            }

            // Only include if there's at least a label
            if (!string.IsNullOrEmpty(label))
            {
                fields[property.Name] = new SchemaOverlayFieldInfo(label, description);
            }
        }
    }
}
