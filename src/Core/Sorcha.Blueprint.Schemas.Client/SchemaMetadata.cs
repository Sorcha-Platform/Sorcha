// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Schemas.Client;

/// <summary>
/// Metadata about a JSON Schema document in the library
/// </summary>
public class SchemaMetadata
{
    /// <summary>
    /// Unique identifier for this schema (URI format recommended)
    /// </summary>
    [JsonPropertyName("$id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable title for the schema
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Description of what data this schema defines
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Schema version
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Category/domain of the schema (e.g., "finance", "healthcare", "identity")
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Tags for search and discovery
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Source of the schema
    /// </summary>
    [JsonPropertyName("source")]
    public SchemaSource Source { get; set; } = SchemaSource.Local;

    /// <summary>
    /// URL to the full schema document (if remote)
    /// </summary>
    [JsonPropertyName("schemaUrl")]
    public string? SchemaUrl { get; set; }

    /// <summary>
    /// Author/maintainer of the schema
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// License information
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// When this schema was added to the library
    /// </summary>
    [JsonPropertyName("addedAt")]
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether this schema is a favorite/starred
    /// </summary>
    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; } = false;

    /// <summary>
    /// Number of times this schema has been used
    /// </summary>
    [JsonPropertyName("usageCount")]
    public int UsageCount { get; set; } = 0;
}

/// <summary>
/// Source type for a schema
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchemaSource
{
    /// <summary>
    /// Built-in schema shipped with Sorcha
    /// </summary>
    BuiltIn,

    /// <summary>
    /// User-defined local schema
    /// </summary>
    Local,

    /// <summary>
    /// Retrieved from SchemaStore.org
    /// </summary>
    SchemaStore,

    /// <summary>
    /// Retrieved from Blueprint service
    /// </summary>
    BlueprintService,

    /// <summary>
    /// Custom external URL
    /// </summary>
    External
}
