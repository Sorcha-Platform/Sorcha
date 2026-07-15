// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// A UI control element for form display
/// Links render element type to data scope
/// </summary>
public class Control
{
    /// <summary>
    /// The display type for this element
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("type")]
    public ControlTypes ControlType { get; set; } = ControlTypes.Label;

    /// <summary>
    /// Label for this control
    /// </summary>
    [DataAnnotations.MaxLength(100)]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Reference to the data values element (likely a DID)
    /// </summary>
    [DataAnnotations.MaxLength(250)]
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Determines the layout of this and embedded controls
    /// </summary>
    [JsonPropertyName("layout")]
    public LayoutTypes Layout { get; set; } = LayoutTypes.VerticalLayout;

    /// <summary>
    /// Additional properties for the control
    /// </summary>
    [JsonPropertyName("properties")]
    public JsonDocument? Properties { get; set; }

    /// <summary>
    /// JSON Schema for validating data in this control/form
    /// </summary>
    [JsonPropertyName("schema")]
    public JsonNode? Schema { get; set; }

    /// <summary>
    /// Per-field help text override (Markdown, max 500 chars).
    /// Takes priority over schema property description for this control.
    /// </summary>
    [DataAnnotations.MaxLength(500)]
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }

    /// <summary>
    /// Sub-elements or sub-controls
    /// </summary>
    [JsonPropertyName("elements")]
    public List<Control> Elements { get; set; } = [];

    /// <summary>
    /// Conditional display rules based on JSON Logic
    /// </summary>
    [Obsolete("Use Rule property instead. Conditions will be removed in a future version.")]
    [JsonPropertyName("conditions")]
    public List<JsonNode> Conditions { get; set; } = [];

    /// <summary>
    /// JSON Forms rule for conditional display (SHOW/HIDE/ENABLE/DISABLE)
    /// </summary>
    [JsonPropertyName("rule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FormRule? Rule { get; set; }

    /// <summary>
    /// Extensible options bag for renderer hints (JSON Forms convention)
    /// </summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonDocument? Options { get; set; }
}

/// <summary>
/// Layout types for controls
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LayoutTypes
{
    /// <summary>
    /// Arranges elements vertically (stacked top to bottom)
    /// </summary>
    VerticalLayout,

    /// <summary>
    /// Arranges elements horizontally (side by side)
    /// </summary>
    HorizontalLayout,

    /// <summary>
    /// Groups multiple elements together (many items)
    /// </summary>
    Group,

    /// <summary>
    /// Categorization layout (one of many selections)
    /// </summary>
    Categorization
}

/// <summary>
/// Control types for form elements
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ControlTypes
{
    /// <summary>
    /// Container control for organizing other controls
    /// </summary>
    [DataAnnotations.Display(Name = "Layout")]
    Layout,

    /// <summary>
    /// Read-only text label for displaying information
    /// </summary>
    [DataAnnotations.Display(Name = "Label")]
    Label,

    /// <summary>
    /// Single-line text input field
    /// </summary>
    [DataAnnotations.Display(Name = "Single Line Text")]
    TextLine,

    /// <summary>
    /// Multi-line text input area for longer content
    /// </summary>
    [DataAnnotations.Display(Name = "Text Area")]
    TextArea,

    /// <summary>
    /// Numeric input field for integer or decimal values
    /// </summary>
    [DataAnnotations.Display(Name = "Number")]
    Numeric,

    /// <summary>
    /// Date and time picker control
    /// </summary>
    [DataAnnotations.Display(Name = "Date/Time")]
    DateTime,

    /// <summary>
    /// File upload control for selecting and uploading files
    /// </summary>
    [DataAnnotations.Display(Name = "File")]
    File,

    /// <summary>
    /// Multiple choice selection (radio buttons or checkboxes)
    /// </summary>
    [DataAnnotations.Display(Name = "Multiple Choice")]
    Choice,

    /// <summary>
    /// Boolean checkbox for true/false values
    /// </summary>
    [DataAnnotations.Display(Name = "Checkbox")]
    Checkbox,

    /// <summary>
    /// Dropdown selection list for choosing from predefined options
    /// </summary>
    [DataAnnotations.Display(Name = "Selection")]
    Selection,

    /// <summary>
    /// Postcode input with address lookup. Dispatched when a string field
    /// carries <c>x-address-lookup: true</c>. The renderer calls the Tenant
    /// Service address lookup endpoints and autofills sibling address fields
    /// from the selected provider. Falls back to plain text when no provider
    /// is configured. Feature 103 US3.
    /// </summary>
    [DataAnnotations.Display(Name = "Postcode Lookup")]
    PostcodeLookup,

    /// <summary>
    /// Read-only holder-key capture. Dispatched when an object field carries
    /// <c>format: "sorcha-holder-key"</c>. The renderer fetches the signed-in
    /// citizen's public delivery keys from the Wallet Service
    /// (<c>GET /api/v1/wallet/holder-keys</c>) and writes the sibling pointers
    /// <c>/{field}/holderJwk</c>, <c>/{field}/encryptionPublicKey</c>, and
    /// <c>/{field}/algorithm</c> into the form payload so a cross-node credential
    /// can be bound and delivered to the citizen. Public material only — the user
    /// cannot edit it. Feature 137 (cross-node submission, C3).
    /// </summary>
    [DataAnnotations.Display(Name = "Holder Key")]
    HolderKey,

    /// <summary>
    /// Read-only device-key capture. Dispatched when an object field carries
    /// <c>format: "sorcha-device-key"</c>. Citizen OID4VP device-bound <c>cnf</c>
    /// (Phase 1, #1195, Option A): the renderer writes THIS DEVICE's public signing
    /// JWK into the <c>/{field}/holderJwk</c> slot so the issued SD-JWT binds
    /// <c>cnf.jwk</c> to the device key, while the wallet's delivery keys
    /// (<c>/{field}/encryptionPublicKey</c>, <c>/{field}/algorithm</c>) still come
    /// from the Wallet Service holder-keys endpoint for the on-register AEAD envelope.
    /// A device-signed KB-JWT then verifies against <c>cnf</c> under standard OID4VP.
    /// Public material only — the user cannot edit it.
    /// </summary>
    [DataAnnotations.Display(Name = "Device Key")]
    DeviceKey
}
