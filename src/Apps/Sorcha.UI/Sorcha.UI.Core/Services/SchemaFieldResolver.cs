// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Schemas;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Resolves schema fields from action DataSchemas for editor dropdowns.
/// Wraps SchemaFieldExtractor to provide a convenient UI-level API that
/// takes an action's DataSchemas collection and returns field metadata.
/// </summary>
public class SchemaFieldResolver
{
    /// <summary>
    /// Resolves all fields from the given data schemas.
    /// Extracts property names, types, descriptions, required flags, and constraints
    /// for use in condition/calculation editor dropdowns.
    /// </summary>
    /// <param name="dataSchemas">Collection of JSON Schema documents from an action's DataSchemas</param>
    /// <returns>Combined list of schema fields across all schemas</returns>
    public List<SchemaField> ResolveFields(IEnumerable<JsonDocument>? dataSchemas)
    {
        if (dataSchemas is null)
            return [];

        var result = new List<SchemaField>();

        foreach (var schema in dataSchemas)
        {
            var fields = SchemaFieldExtractor.ExtractFields(schema);
            foreach (var field in fields)
            {
                result.Add(new SchemaField
                {
                    Name = field.Path,
                    Type = field.Type,
                    Description = field.Description,
                    IsRequired = field.IsRequired,
                    Constraints = BuildConstraints(field)
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves field names only (for dropdown lists).
    /// </summary>
    /// <param name="dataSchemas">Collection of JSON Schema documents</param>
    /// <returns>List of field path names (e.g., "address.street", "amount")</returns>
    public List<string> ResolveFieldNames(IEnumerable<JsonDocument>? dataSchemas)
    {
        return ResolveFields(dataSchemas).Select(f => f.Name).ToList();
    }

    private static Dictionary<string, string>? BuildConstraints(SchemaFieldInfo field)
    {
        var constraints = new Dictionary<string, string>();

        if (field.MinLength.HasValue)
            constraints["minLength"] = field.MinLength.Value.ToString();
        if (field.MaxLength.HasValue)
            constraints["maxLength"] = field.MaxLength.Value.ToString();
        if (field.Minimum.HasValue)
            constraints["minimum"] = field.Minimum.Value.ToString();
        if (field.Maximum.HasValue)
            constraints["maximum"] = field.Maximum.Value.ToString();
        if (field.Pattern is not null)
            constraints["pattern"] = field.Pattern;
        if (field.EnumValues is { Length: > 0 })
            constraints["enum"] = string.Join(", ", field.EnumValues);

        return constraints.Count > 0 ? constraints : null;
    }
}

/// <summary>
/// Extracted field from a JSON Schema for use in UI editors.
/// </summary>
public class SchemaField
{
    /// <summary>
    /// Property name/path from schema (e.g., "amount", "address.street")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema type (string, number, boolean, object, array)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Schema property description (help text fallback)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether field is in schema's required array
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Extracted constraints (minLength, maxLength, pattern, enum, etc.)
    /// </summary>
    public Dictionary<string, string>? Constraints { get; set; }
}
