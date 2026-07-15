// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Issuer-defined visual template for how a credential appears in wallets.
/// </summary>
public class CredentialDisplayConfig
{
    /// <summary>
    /// Card background color as hex (e.g., "#1976D2").
    /// </summary>
    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#1976D2";

    /// <summary>
    /// Card text color as hex (e.g., "#FFFFFF").
    /// </summary>
    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// MudBlazor icon name for the credential type (e.g., "Certificate").
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "Certificate";

    /// <summary>
    /// Card layout template: Standard, Compact, or Ticket.
    /// </summary>
    [JsonPropertyName("cardLayout")]
    public string CardLayout { get; set; } = "Standard";

    /// <summary>
    /// Claims to highlight on the card face. Key = claim JSON path, Value = display label.
    /// </summary>
    [JsonPropertyName("highlightClaims")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? HighlightClaims { get; set; }

    /// <summary>
    /// Authored human-readable credential name shown on the card (e.g. "Assured Identity"),
    /// decoupled from the vct URI. Sourced from CredentialIssuanceConfig.DisplayName at issuance.
    /// </summary>
    [JsonPropertyName("credentialName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CredentialName { get; set; }
}
