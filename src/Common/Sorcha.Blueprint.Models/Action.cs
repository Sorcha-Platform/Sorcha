// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Defines an action within a Blueprint workflow
/// Specifies what data is received and returned for the next participant
/// </summary>
public class Action
{
    /// <summary>
    /// JSON-LD type (ActivityStreams Activity type)
    /// </summary>
    [JsonPropertyName("@type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JsonLdType { get; set; }

    /// <summary>
    /// The Action ID (sequence number within the blueprint)
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.Range(0, int.MaxValue)]
    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    /// <summary>
    /// Reference to the previous transaction ID
    /// Supports both legacy format (transaction hash) and DID URI format
    /// Preferred format: did:sorcha:register:{registerId}/tx/{txId}
    /// See: docs/reference/blockchain-transaction-format.md
    /// </summary>
    [DataAnnotations.MaxLength(256)]
    [JsonPropertyName("previousTxId")]
    public string PreviousTxId { get; set; } = string.Empty;

    /// <summary>
    /// Blueprint ID this action belongs to
    /// </summary>
    [DataAnnotations.MaxLength(64)]
    [JsonPropertyName("blueprint")]
    public string BlueprintId { get; set; } = string.Empty;

    /// <summary>
    /// Title for this action (e.g., "Apply", "Endorse", "Approve")
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(100)]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this action does
    /// </summary>
    [DataAnnotations.MaxLength(2048)]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Address of the sender (may be a stealth/derived address)
    /// In ActivityStreams terms, this is the "actor"
    /// </summary>
    [DataAnnotations.MaxLength(100)]
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// Target participant(s) for this action (ActivityStreams "target")
    /// </summary>
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }

    /// <summary>
    /// Potential participants to enact this step (supports branching/routing)
    /// </summary>
    [JsonPropertyName("participants")]
    public IEnumerable<Condition>? Participants { get; set; } = [];

    /// <summary>
    /// List of required action data IDs
    /// </summary>
    [JsonPropertyName("requiredActionData")]
    public IEnumerable<string> RequiredActionData { get; set; } = [];

    /// <summary>
    /// Additional recipients (excludes the sender)
    /// </summary>
    [DataAnnotations.MinLength(0)]
    [JsonPropertyName("additionalRecipients")]
    public IEnumerable<string> AdditionalRecipients { get; set; } = [];

    /// <summary>
    /// List of disclosures defining data visibility rules.
    /// At least one disclosure is required per action.
    /// </summary>
    [DataAnnotations.MinLength(1)]
    [JsonPropertyName("disclosures")]
    public IEnumerable<Disclosure> Disclosures { get; set; } = [];

    /// <summary>
    /// JSON of data elements received from previous action
    /// </summary>
    [JsonPropertyName("previousData")]
    public JsonDocument? PreviousData { get; set; }

    /// <summary>
    /// JSON schemas for data elements to pass to next participant
    /// </summary>
    [DataAnnotations.MinLength(0)]
    [JsonPropertyName("dataSchemas")]
    public IEnumerable<JsonDocument>? DataSchemas { get; set; }

    /// <summary>
    /// Routing condition (JSON Logic) that resolves to the next action number
    /// </summary>
    [JsonPropertyName("condition")]
    public JsonNode? Condition { get; set; } = JsonNode.Parse("{\"==\":[0,0]}");

    /// <summary>
    /// User-defined calculations performed on submitted data (JSON Logic)
    /// </summary>
    [JsonPropertyName("calculations")]
    public Dictionary<string, JsonNode>? Calculations { get; set; } = [];

    /// <summary>
    /// Timestamp when the action was published (ISO 8601)
    /// </summary>
    [JsonPropertyName("published")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? Published { get; set; }

    /// <summary>
    /// Additional JSON-LD properties for extended action information
    /// </summary>
    [JsonPropertyName("additionalProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonNode>? AdditionalProperties { get; set; }

    /// <summary>
    /// Specifies the format of data presentation (UI form)
    /// </summary>
    [JsonPropertyName("form")]
    public Control? Form { get; set; } = new()
    {
        ControlType = ControlTypes.Layout,
        Layout = LayoutTypes.VerticalLayout
    };

    /// <summary>
    /// Rejection routing configuration.
    /// Defines where the workflow routes when a participant rejects this action.
    /// If not specified, rejection is not allowed for this action.
    /// </summary>
    [JsonPropertyName("rejectionConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RejectionConfig? RejectionConfig { get; set; }

    /// <summary>
    /// Required prior action IDs for state reconstruction.
    /// When executing this action, data from these prior actions will be fetched
    /// and decrypted to build the accumulated state for routing evaluation.
    /// If not specified, only the immediately preceding action is used.
    /// </summary>
    [JsonPropertyName("requiredPriorActions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<int>? RequiredPriorActions { get; set; }

    /// <summary>
    /// Whether this is a starting action that can initiate a workflow instance.
    /// At least one action in a blueprint must be a starting action.
    /// </summary>
    [JsonPropertyName("isStartingAction")]
    public bool IsStartingAction { get; set; } = false;

    /// <summary>
    /// Routing rules to determine next action(s).
    /// Evaluated in order - first matching condition wins.
    /// Supports parallel branches via multiple nextActionIds.
    /// </summary>
    [JsonPropertyName("routes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<Route>? Routes { get; set; }

    /// <summary>
    /// Credential requirements that must be satisfied before this action can be executed.
    /// All requirements are combined with AND logic — all must be satisfied.
    /// </summary>
    [JsonPropertyName("credentialRequirements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<CredentialRequirement>? CredentialRequirements { get; set; }

    /// <summary>
    /// Configuration for minting a verifiable credential when this action is executed.
    /// The credential is signed by the executing participant's wallet key.
    /// </summary>
    [JsonPropertyName("credentialIssuanceConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CredentialIssuanceConfig? CredentialIssuanceConfig { get; set; }
}
