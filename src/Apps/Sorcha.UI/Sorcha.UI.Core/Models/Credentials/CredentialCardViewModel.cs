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

    /// <summary>Claim names that may be selectively disclosed during presentation.</summary>
    public List<string> DisclosableClaims { get; set; } = [];

    /// <summary>Whether the credential is awaiting acceptance by the holder.</summary>
    public bool IsPending { get; set; }

    /// <summary>
    /// Feature 106 — blueprint id of the issuing flow. Populated from the
    /// Wallet Service credential list response so the MyCredentials PENDING
    /// tab can deep-link into the originating instance on accept.
    /// </summary>
    public string? IssuanceBlueprintId { get; set; }

    /// <summary>
    /// Feature 106 SC-003 — instance id of the issuing flow. Used by accept/decline
    /// to execute Action 3 on the correct blueprint instance.
    /// </summary>
    public string? IssuanceInstanceId { get; set; }

    /// <summary>
    /// Feature 106 SC-003 — action id of the issuance action.
    /// </summary>
    public string? IssuanceActionId { get; set; }

    /// <summary>
    /// Feature 106 SC-003 — action id of the holder's claim action (e.g. "3").
    /// This is the action the holder executes to accept or reject the credential.
    /// </summary>
    public string? ClaimActionId { get; set; }

    /// <summary>
    /// Feature 106 SC-003 — register id where the issuance transaction was sealed.
    /// </summary>
    public string? RegisterId { get; set; }

    /// <summary>Name of the blueprint that produced this credential, if applicable.</summary>
    public string? OriginatingBlueprintName { get; set; }

    /// <summary>Display name of the issuing organisation.</summary>
    public string? IssuerOrgName { get; set; }

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
