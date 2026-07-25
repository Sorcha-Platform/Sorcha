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

    /// <summary>
    /// The credential type as a human reads it — "Assured Identity", not
    /// "AssuredIdentityCredential". Falls back to <see cref="Type"/>.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// One line naming what is inside, e.g. "Name, date of birth, address".
    /// Claim NAMES only — a list view must never print claim values, or a
    /// holder's address is legible to anyone glancing at their screen.
    /// </summary>
    public string ClaimSummary { get; set; } = string.Empty;
    public string IssuerDid { get; set; } = string.Empty;
    public string IssuerName { get; set; } = string.Empty;
    public string SubjectDid { get; set; } = string.Empty;
    public string Status { get; set; } = CredentialStatus.Active;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string UsagePolicy { get; set; } = "Reusable";
    public int? MaxPresentations { get; set; }
    public int PresentationCount { get; set; }

    /// <summary>
    /// Claims spotlighted on the card face, in display order. Each entry carries BOTH
    /// the display label (what the citizen reads) and the underlying claim name / JSON
    /// pointer first segment (what governs disclosability) — a flat label-keyed
    /// dictionary cannot express both, which is how the padlock ended up checking
    /// <c>DisclosableClaims.Contains(label)</c> against a set of raw claim names.
    /// </summary>
    public List<HighlightClaimEntry> HighlightClaims { get; set; } = new();
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
/// A single spotlighted claim on a credential card. <see cref="ClaimName"/> is the
/// raw top-level claim name (or a JSON pointer's first segment) — the identifier
/// <see cref="CredentialCardViewModel.DisclosableClaims"/> is expressed in.
/// <see cref="Label"/> is the issuer-facing display text, which may differ (e.g. a
/// pointer <c>/address/town</c> labelled "Home town"). The padlock in
/// <c>CredentialAcceptCard</c> MUST check <see cref="ClaimName"/>, never <see cref="Label"/>.
/// </summary>
public sealed record HighlightClaimEntry(string ClaimName, string Label, string Value);

/// <summary>
/// Display configuration for credential card rendering.
/// </summary>
public class CredentialDisplayViewModel
{
    public string BackgroundColor { get; set; } = "#1976D2";
    public string TextColor { get; set; } = "#FFFFFF";
    public string Icon { get; set; } = "Certificate";
    public string CardLayout { get; set; } = "Standard";

    /// <summary>
    /// The issuer-authored human-readable credential name (e.g. "Assured Identity"),
    /// mirroring <c>CredentialDisplayConfig.CredentialName</c> on the blueprint side.
    /// </summary>
    /// <remarks>
    /// Issue #1278: this property was MISSING, so the authored name sitting inside
    /// <c>displayConfigJson</c> was deserialised away and silently discarded — leaving every web
    /// credential surface with nothing but the <c>vct</c> to show. Post-#1187 the vct is a URI
    /// <i>by design</i>, so those surfaces rendered
    /// <c>https://sorcha.dev/vc/assured-identity/v1</c> as the title. That single omission is the
    /// root cause on all three surfaces (offer card, wallet list card, view dialog).
    /// </remarks>
    public string? CredentialName { get; set; }

    /// <summary>
    /// Issuer-specified claim spotlight: key = JSON pointer into the claim
    /// payload (e.g. "/sustainabilityScore"), value = display label
    /// ("Sustainability"). Mirrors <c>CredentialDisplayConfig.HighlightClaims</c>
    /// on the blueprint side. Optional — when absent the wallet picks a
    /// reasonable default subset.
    /// </summary>
    public Dictionary<string, string>? HighlightClaims { get; set; }
}

/// <summary>
/// Detailed view of a credential including all claims and metadata.
/// </summary>
public class CredentialDetailViewModel
{
    public string CredentialId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable credential name for the dialog title and id-card face — the issuer-authored
    /// name where available, else a humanised form of the type. Never the raw <c>vct</c>.
    /// </summary>
    /// <remarks>
    /// Issue #1278: the view dialog rendered <c>Type</c> directly, so post-#1187 (where the vct is a
    /// URI by design) it showed <c>https://sorcha.dev/vc/assured-identity/v1</c> — twice, clipped
    /// mid-word, with a horizontal scrollbar.
    /// </remarks>
    public string DisplayName { get; set; } = string.Empty;

    public string IssuerDid { get; set; } = string.Empty;

    /// <summary>Friendly issuer name (org name where known, else derived from the DID) for the card header.</summary>
    public string IssuerName { get; set; } = string.Empty;
    public string SubjectDid { get; set; } = string.Empty;
    public string Status { get; set; } = CredentialStatus.Active;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string UsagePolicy { get; set; } = "Reusable";
    public int? MaxPresentations { get; set; }
    public int PresentationCount { get; set; }
    public Dictionary<string, object> Claims { get; set; } = new();

    /// <summary>
    /// Pre-formatted, safe-to-render text for every non-protocol claim — the counterpart
    /// of the card path's <c>StringifyClaimValue</c>/<c>SummariseObject</c> discipline for
    /// the detail dialog. A nested object renders as structural name/value pairs and
    /// <c>_</c>-prefixed protocol keys (e.g. <c>_sd</c>, <c>_sd_alg</c>) are dropped at
    /// every level — never raw JSON. Use this for display; <see cref="Claims"/> keeps the
    /// raw values other consumers (e.g. the credential requirement gate's disclosed-claims
    /// payload) still need.
    /// </summary>
    public Dictionary<string, string> DisplayClaims { get; set; } = new();
    public CredentialDisplayViewModel DisplayConfig { get; set; } = new();
    public string? StatusListUrl { get; set; }
    public string? IssuanceBlueprintId { get; set; }
}
