// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// A participant in a Blueprint workflow
/// </summary>
public class Participant : IEquatable<Participant>
{
    /// <summary>
    /// JSON-LD type (Person or Organization)
    /// </summary>
    [JsonPropertyName("@type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JsonLdType { get; set; }

    /// <summary>
    /// Unique identifier for the participant
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.Key]
    [DataAnnotations.MaxLength(64)]
    [Json.Schema.Generation.Required]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Friendly name for the participant
    /// </summary>
    [DataAnnotations.Required(ErrorMessage = "Participant name is required.")]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(100)]
    [Json.Schema.Generation.Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Organization the participant belongs to (optional)
    /// </summary>
    [DataAnnotations.MaxLength(200)]
    [JsonPropertyName("organisation")]
    public string Organisation { get; set; } = string.Empty;

    /// <summary>
    /// Wallet address of the participant (optional — resolved dynamically at execution time when absent)
    /// </summary>
    [DataAnnotations.MaxLength(100)]
    [JsonPropertyName("walletAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WalletAddress { get; set; }

    /// <summary>
    /// Decentralized Identifier (DID) URI for the participant
    /// Example: did:example:123456789abcdefghi
    /// </summary>
    [DataAnnotations.MaxLength(200)]
    [JsonPropertyName("didUri")]
    public string? DidUri { get; set; }

    /// <summary>
    /// Verifiable Credential (JSON-LD format) for the participant
    /// Supports W3C Verifiable Credentials standard
    /// </summary>
    [JsonPropertyName("verifiableCredential")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? VerifiableCredential { get; set; }

    /// <summary>
    /// Additional JSON-LD properties for extended participant information
    /// </summary>
    [JsonPropertyName("additionalProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonNode>? AdditionalProperties { get; set; }

    /// <summary>
    /// Role-specific guidance text for this participant (Markdown, max 2000 chars).
    /// Explains what this participant is expected to do in the workflow.
    /// </summary>
    [DataAnnotations.MaxLength(2000)]
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }

    /// <summary>
    /// Whether to use a stealth address for privacy
    /// </summary>
    [JsonPropertyName("useStealthAddress")]
    public bool UseStealthAddress { get; set; } = false;

    public override bool Equals(object? obj) => Equals(obj as Participant);

    public bool Equals(Participant? other)
    {
        return other != null && Id == other.Id && Name == other.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }
}
