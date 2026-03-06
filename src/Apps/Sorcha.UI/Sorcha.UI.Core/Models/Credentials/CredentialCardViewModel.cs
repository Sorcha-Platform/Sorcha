// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// View model for displaying a credential as a card in the wallet UI.
/// </summary>
public class CredentialCardViewModel
{
    public string CredentialId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string IssuerDid { get; set; } = string.Empty;
    public string IssuerName { get; set; } = string.Empty;
    public string SubjectDid { get; set; } = string.Empty;
    public string Status { get; set; } = CredentialStatus.Active;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string UsagePolicy { get; set; } = "Reusable";
    public int? MaxPresentations { get; set; }
    public int PresentationCount { get; set; }
    public Dictionary<string, string> HighlightClaims { get; set; } = new();
    public CredentialDisplayViewModel DisplayConfig { get; set; } = new();
    public List<string> AvailableActions { get; set; } = new();

    /// <summary>
    /// Whether the credential expires within 30 days.
    /// </summary>
    public bool IsExpiringSoon =>
        ExpiresAt.HasValue &&
        ExpiresAt.Value > DateTimeOffset.UtcNow &&
        ExpiresAt.Value <= DateTimeOffset.UtcNow.AddDays(30);
}

/// <summary>
/// Display configuration for credential card rendering.
/// </summary>
public class CredentialDisplayViewModel
{
    public string BackgroundColor { get; set; } = "#1976D2";
    public string TextColor { get; set; } = "#FFFFFF";
    public string Icon { get; set; } = "Certificate";
    public string CardLayout { get; set; } = "Standard";
}

/// <summary>
/// Detailed view of a credential including all claims and metadata.
/// </summary>
public class CredentialDetailViewModel
{
    public string CredentialId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string IssuerDid { get; set; } = string.Empty;
    public string SubjectDid { get; set; } = string.Empty;
    public string Status { get; set; } = CredentialStatus.Active;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string UsagePolicy { get; set; } = "Reusable";
    public int? MaxPresentations { get; set; }
    public int PresentationCount { get; set; }
    public Dictionary<string, object> Claims { get; set; } = new();
    public CredentialDisplayViewModel DisplayConfig { get; set; } = new();
    public string? StatusListUrl { get; set; }
    public string? IssuanceBlueprintId { get; set; }
}
