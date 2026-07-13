// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Defines conditional routing to the next action(s) in a workflow.
/// Routes are evaluated in order - first matching condition wins.
/// </summary>
public class Route
{
    /// <summary>
    /// Unique identifier for this route within the action
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MaxLength(100)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Next action ID(s) to route to.
    /// Multiple IDs create parallel branches.
    /// Empty list indicates workflow completion.
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("nextActionIds")]
    public IEnumerable<int> NextActionIds { get; set; } = [];

    /// <summary>
    /// JSON Logic condition to evaluate.
    /// Evaluated against the accumulated state from prior actions.
    /// If null or not specified, this is treated as a default route.
    /// </summary>
    [JsonPropertyName("condition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Condition { get; set; }

    /// <summary>
    /// Whether this is the default route when no conditions match.
    /// Only one route per action should be marked as default.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Optional description of when this route is taken
    /// </summary>
    [DataAnnotations.MaxLength(500)]
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Optional deadline for parallel branch completion.
    /// Specified as ISO 8601 duration (e.g., "P7D" for 7 days).
    /// Only applicable when nextActionIds contains multiple IDs.
    /// </summary>
    [JsonPropertyName("branchDeadline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchDeadline { get; set; }

    /// <summary>
    /// Optional map from source JSON Pointer (in the current action's execution
    /// result document) to target JSON Pointer (in the next action's starting
    /// payload). Evaluated when this route fires during action execution. Absent
    /// source paths are silently skipped. Applies to every next action listed in
    /// <see cref="NextActionIds"/>.
    /// </summary>
    /// <remarks>
    /// The source document available for mapping is:
    /// <code>
    /// {
    ///   "payload":      &lt;submitted action payload&gt;,
    ///   "calculations": &lt;engine calculate-step output&gt;,
    ///   "haip":         &lt;HAIP mint output when present&gt;
    /// }
    /// </code>
    /// Both keys and values MUST be valid JSON Pointer strings (RFC 6901),
    /// leading slash required. Introduced in Feature 104 wave 14a.
    /// </remarks>
    [JsonPropertyName("outputMapping")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? OutputMapping { get; set; }

    /// <summary>
    /// Optional decision-notice declaration (Feature 183). When this route is the one taken,
    /// a durable inbox entry carrying the on-brand reason is written to the named recipient
    /// participant — making an autonomous decision (e.g. an AIAS reject) visible to the
    /// applicant across sessions and devices. Reusable by any blueprint route.
    /// </summary>
    [JsonPropertyName("x-decision-notice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DecisionNotice? DecisionNotice { get; set; }
}

/// <summary>
/// Declares that taking a route notifies a participant with a durable, reasoned notice
/// (Feature 183). Carried on a <see cref="Route"/> as the <c>x-decision-notice</c> extension.
/// </summary>
public class DecisionNotice
{
    /// <summary>
    /// Participant id to notify (resolved to a wallet via the instance's participant bindings).
    /// For AIAS this is the starting participant, <c>"citizen"</c>.
    /// </summary>
    [JsonPropertyName("recipientParticipantId")]
    public string RecipientParticipantId { get; set; } = string.Empty;

    /// <summary>
    /// JSON Pointer into the submitted action payload for the human-readable reason
    /// (e.g. <c>/verificationNotes</c>). Surfaced as the notification summary.
    /// </summary>
    [JsonPropertyName("reasonField")]
    public string ReasonField { get; set; } = string.Empty;

    /// <summary>Notification title (e.g. "AIAS could not assure your identity").</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Inbox severity. Defaults to <c>Warning</c> when unspecified.</summary>
    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; set; }
}
