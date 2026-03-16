// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Linked external instruction document for translations.
/// References an instruction document at a DID URI or URL with locale and version.
/// </summary>
public class InstructionSet
{
    /// <summary>
    /// Language code (BCP 47, e.g., "fr-FR"). Required.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MaxLength(10)]
    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// DID URI or URL to the instruction document. Required.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MaxLength(500)]
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Version of the translation document
    /// </summary>
    [DataAnnotations.MaxLength(20)]
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }
}
