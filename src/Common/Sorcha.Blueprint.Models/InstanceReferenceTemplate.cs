// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Defines how to generate a human-readable compound reference for workflow instances.
/// The reference is auto-generated from first-action payload fields and stored as
/// public metadata on the instance (e.g., "CP-RIV-14-A7K3").
/// </summary>
public class InstanceReferenceTemplate
{
    /// <summary>
    /// Short prefix identifying the workflow type (1-5 uppercase alpha characters).
    /// Example: "CP" for Construction Permit, "PA" for Planning Application.
    /// </summary>
    [DataAnnotations.Required(ErrorMessage = "Instance reference prefix is required.")]
    [DataAnnotations.RegularExpression(@"^[A-Z]{1,5}$", ErrorMessage = "Prefix must be 1-5 uppercase letters.")]
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of field components that form the reference body.
    /// Each component extracts a segment from an action payload field.
    /// Minimum 1, maximum 5 components.
    /// </summary>
    [DataAnnotations.Required(ErrorMessage = "At least one reference component is required.")]
    [DataAnnotations.MinLength(1, ErrorMessage = "At least one reference component is required.")]
    [DataAnnotations.MaxLength(5, ErrorMessage = "Maximum 5 reference components allowed.")]
    [JsonPropertyName("components")]
    public List<ReferenceComponent> Components { get; set; } = [];
}

/// <summary>
/// A single component of an instance reference, extracting a display segment
/// from a payload field using a configurable transform.
/// </summary>
public class ReferenceComponent
{
    /// <summary>
    /// JSON Pointer to the payload field (e.g., "/projectName", "/siteAddress").
    /// Must reference a field from the starting action's schema.
    /// </summary>
    [DataAnnotations.Required(ErrorMessage = "Field path is required.")]
    [DataAnnotations.RegularExpression(@"^/[^/]+$", ErrorMessage = "Field must be a single-level JSON Pointer (e.g., '/projectName'). Nested paths are not supported.")]
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Transform to apply when extracting the display segment.
    /// </summary>
    [DataAnnotations.Required(ErrorMessage = "Transform is required.")]
    [JsonPropertyName("transform")]
    public ReferenceTransform Transform { get; set; } = ReferenceTransform.FirstWord;

    /// <summary>
    /// Maximum characters for this component segment (2-10, default 3).
    /// </summary>
    [DataAnnotations.Range(2, 10, ErrorMessage = "Chars must be between 2 and 10.")]
    [JsonPropertyName("chars")]
    public int Chars { get; set; } = 3;
}

/// <summary>
/// Available transforms for instance reference components.
/// JSON values: "first-word", "truncate" (kebab-case).
/// All output is uppercased regardless of transform.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceTransform>))]
public enum ReferenceTransform
{
    /// <summary>Extract the first word (split on whitespace), truncate to <see cref="ReferenceComponent.Chars"/>.</summary>
    FirstWord,

    /// <summary>Truncate the full value to <see cref="ReferenceComponent.Chars"/> characters.</summary>
    Truncate
}
