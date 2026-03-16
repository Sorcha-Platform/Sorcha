// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Top-level instructions container for a Blueprint.
/// Holds overview text, per-action and per-participant guidance,
/// linked translation sets, and governance role definitions.
/// </summary>
public class BlueprintInstructions
{
    /// <summary>
    /// Markdown overview of the workflow purpose and usage
    /// </summary>
    [DataAnnotations.MaxLength(5000)]
    [JsonPropertyName("overview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Overview { get; set; }

    /// <summary>
    /// Primary language code (BCP 47, e.g., "en-GB")
    /// </summary>
    [DataAnnotations.MaxLength(10)]
    [JsonPropertyName("locale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Locale { get; set; }

    /// <summary>
    /// Per-action guidance keyed by action ID
    /// </summary>
    [JsonPropertyName("actionInstructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int, string>? ActionInstructions { get; set; }

    /// <summary>
    /// Per-participant role guidance keyed by participant name
    /// </summary>
    [JsonPropertyName("participantInstructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? ParticipantInstructions { get; set; }

    /// <summary>
    /// Linked external instruction documents for translations
    /// </summary>
    [JsonPropertyName("instructionSets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<InstructionSet>? InstructionSets { get; set; }

    /// <summary>
    /// Blueprint-defined governance participants keyed by role name.
    /// Values are DID URIs or participant IDs.
    /// Blueprint-defined roles take precedence over org-admin role assignments.
    /// </summary>
    [JsonPropertyName("governanceRoles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? GovernanceRoles { get; set; }
}
