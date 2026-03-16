// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Blueprint - Defines a workflow with participants, actions, and data flow
/// </summary>
public class Blueprint : IEquatable<Blueprint>
{
    /// <summary>
    /// JSON-LD context for semantic web integration
    /// </summary>
    [JsonPropertyName("@context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? JsonLdContext { get; set; }

    /// <summary>
    /// JSON-LD type for semantic classification
    /// </summary>
    [JsonPropertyName("@type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JsonLdType { get; set; }

    /// <summary>
    /// Unique identifier for the Blueprint
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MaxLength(64)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The Blueprint title
    /// </summary>
    [DataAnnotations.Required(AllowEmptyStrings = false, ErrorMessage = "Blueprint title must be populated.")]
    [DataAnnotations.MinLength(3)]
    [DataAnnotations.MaxLength(200)]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Description of the Blueprint's purpose
    /// </summary>
    [DataAnnotations.Required(AllowEmptyStrings = false, ErrorMessage = "Blueprint description must be populated.")]
    [DataAnnotations.MinLength(5)]
    [DataAnnotations.MaxLength(2000)]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Version number
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Embedded reference data schema documents
    /// </summary>
    [JsonPropertyName("dataSchemas")]
    public List<JsonDocument>? DataSchemas { get; set; }

    /// <summary>
    /// List of participants (minimum 2 required)
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(2)]
    [Json.Schema.Generation.MinItems(2)]
    [JsonPropertyName("participants")]
    public List<Participant> Participants { get; set; } = [];

    /// <summary>
    /// Actions defining the workflow steps (minimum 1 required)
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [Json.Schema.Generation.MinItems(1)]
    [JsonPropertyName("actions")]
    public List<Models.Action> Actions { get; set; } = [];

    /// <summary>
    /// The organization that owns this blueprint.
    /// Used for org-scoped access control.
    /// </summary>
    [JsonPropertyName("organizationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Semantic major version number (structural changes increment this).
    /// Use VersionMajor.VersionMinor for display (e.g., "v2.1").
    /// </summary>
    [JsonPropertyName("versionMajor")]
    public int VersionMajor { get; set; } = 1;

    /// <summary>
    /// Semantic minor version number (documentation-only changes increment this; resets on major bump).
    /// </summary>
    [JsonPropertyName("versionMinor")]
    public int VersionMinor { get; set; } = 0;

    /// <summary>
    /// Instructions container with overview, per-action/per-participant guidance,
    /// linked translations, and governance roles.
    /// </summary>
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlueprintInstructions? Instructions { get; set; }

    /// <summary>
    /// Metadata for the blueprint
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// When the blueprint was created
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the blueprint was last updated
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Determines whether the specified object is equal to this blueprint.
    /// Compares Id, Title, Description, Version, Participants, and Actions.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if the objects are equal, otherwise false.</returns>
    public override bool Equals(object? obj) => Equals(obj as Blueprint);

    /// <summary>
    /// Determines whether the specified blueprint is equal to this blueprint.
    /// Compares Id, Title, Description, Version, Participants (sequence), and Actions (sequence).
    /// </summary>
    /// <param name="other">The blueprint to compare.</param>
    /// <returns>True if the blueprints are equal, otherwise false.</returns>
    public bool Equals(Blueprint? other)
    {
        return other != null &&
               Id == other.Id &&
               Title == other.Title &&
               Description == other.Description &&
               Version == other.Version &&
               (Participants == other.Participants ||
                Participants.SequenceEqual(other.Participants)) &&
               (Actions == other.Actions ||
                Actions.SequenceEqual(other.Actions));
    }

    /// <summary>
    /// Returns a hash code for this blueprint based on its identity properties.
    /// Note: Collections are excluded as their instance hash codes don't reflect content equality.
    /// </summary>
    /// <returns>A hash code for the blueprint.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Title, Description, Version);
    }
}
