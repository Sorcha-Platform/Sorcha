// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Schemas.Client;

/// <summary>
/// Represents a complete JSON Schema document with metadata
/// </summary>
public class SchemaDocument
{
    /// <summary>
    /// Metadata about the schema
    /// </summary>
    [JsonPropertyName("metadata")]
    public SchemaMetadata Metadata { get; set; } = new();

    /// <summary>
    /// The actual JSON Schema document
    /// </summary>
    [JsonPropertyName("schema")]
    public JsonDocument Schema { get; set; } = JsonDocument.Parse("{}");

    /// <summary>
    /// Cached property names extracted from the schema for quick search
    /// </summary>
    [JsonPropertyName("propertyNames")]
    public List<string> PropertyNames { get; set; } = [];

    /// <summary>
    /// Whether the schema has been validated
    /// </summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// Validation errors if any
    /// </summary>
    [JsonPropertyName("validationErrors")]
    public List<string>? ValidationErrors { get; set; }

    /// <summary>
    /// Extract property names from the schema for indexing
    /// </summary>
    public void ExtractPropertyNames()
    {
        PropertyNames.Clear();

        if (Schema.RootElement.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                PropertyNames.Add(property.Name);
            }
        }
    }

    /// <summary>
    /// Get a human-readable summary of the schema
    /// </summary>
    public string GetSummary()
    {
        return $"{Metadata.Title} ({Metadata.Version}) - {PropertyNames.Count} properties";
    }
}
