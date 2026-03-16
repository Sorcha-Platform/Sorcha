// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Semantic version attached to published blueprints in the system register.
/// Major version increments on structural changes; minor version increments on documentation-only changes.
/// </summary>
public class BlueprintVersion
{
    /// <summary>
    /// Structural version number (increments on action/route/schema/participant changes)
    /// </summary>
    [DataAnnotations.Range(0, int.MaxValue)]
    [JsonPropertyName("major")]
    public int Major { get; set; }

    /// <summary>
    /// Documentation version number (increments on instruction/text-only changes, resets on major bump)
    /// </summary>
    [DataAnnotations.Range(0, int.MaxValue)]
    [JsonPropertyName("minor")]
    public int Minor { get; set; }

    /// <summary>
    /// Type of change from previous version: "structural" or "documentation"
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = "structural";

    /// <summary>
    /// SHA-256 hex hash of the blueprint excluding instructions (64 characters)
    /// </summary>
    [DataAnnotations.MaxLength(64)]
    [JsonPropertyName("structuralHash")]
    public string StructuralHash { get; set; } = string.Empty;

    /// <summary>
    /// When this version was published
    /// </summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>
    /// Wallet address of the signer who published this version
    /// </summary>
    [JsonPropertyName("publishedBy")]
    public string PublishedBy { get; set; } = string.Empty;

    /// <summary>
    /// Register transaction ID for this version
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Returns the display string for this version (e.g., "v2.1")
    /// </summary>
    public override string ToString() => $"v{Major}.{Minor}";
}
