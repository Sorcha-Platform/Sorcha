// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Parsed representation of the <c>x-review</c> schema extension declared on a
/// wizard page. Marks the page as a read-only summary of the form's prior
/// pages' values, rendered via a parameterised layout component.
/// </summary>
/// <remarks>
/// Contract: <c>specs/107-assured-identity-v1/contracts/x-review-extension.md</c>.
/// A page carrying this extension MUST NOT declare <c>properties</c> — the
/// review renderer iterates fields from prior pages in the same action.
/// </remarks>
public sealed record XReviewExtension
{
    /// <summary>
    /// Visual layout variant. v1 ships only <see cref="XReviewLayoutVariant.IdCard"/>;
    /// unknown values produce a publish-time warning (<c>WARN_BP_REVIEW_001</c>)
    /// and the renderer falls back to a tabular minimal display.
    /// </summary>
    public required XReviewLayoutVariant Layout { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the renderer generates per-section Edit
    /// controls that navigate back to the originating wizard page while
    /// preserving submitted values. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Editable { get; init; } = true;

    /// <summary>
    /// Issuer and credential labelling that the card header carries.
    /// </summary>
    public required XReviewHeader Header { get; init; }
}

/// <summary>
/// Header metadata for an <see cref="XReviewExtension"/> — rendered as the
/// issuer and credential-type label on the review card.
/// </summary>
public sealed record XReviewHeader
{
    /// <summary>
    /// Issuer organisation display name, e.g. "Government of Scotland".
    /// </summary>
    public required string IssuerName { get; init; }

    /// <summary>
    /// Credential type display name, e.g. "Assured Identity".
    /// </summary>
    public required string CredentialName { get; init; }

    /// <summary>
    /// Visual colour theme. Defaults to <see cref="XReviewColourTheme.IdentityNavy"/>.
    /// </summary>
    public XReviewColourTheme ColourTheme { get; init; } = XReviewColourTheme.IdentityNavy;
}

/// <summary>
/// Layout variants a review page may request. v1 implements <see cref="IdCard"/>
/// only; other variants are reserved for future work and trigger a fallback.
/// </summary>
public enum XReviewLayoutVariant
{
    /// <summary>Stylised credential-card preview of the collected data.</summary>
    IdCard,

    /// <summary>Reserved for future passport-page variant — unimplemented in v1.</summary>
    PassportPage,

    /// <summary>Reserved for future tabular variant — unimplemented in v1.</summary>
    Tabular,

    /// <summary>Reserved for future receipt-style variant — unimplemented in v1.</summary>
    Receipt
}

/// <summary>
/// Colour theme for the <see cref="XReviewLayoutVariant.IdCard"/> layout.
/// Each theme resolves to a CSS custom-property set on the rendered card.
/// </summary>
public enum XReviewColourTheme
{
    /// <summary>Navy/gold palette used by the Assured Identity credential.</summary>
    IdentityNavy,

    /// <summary>Pink/cream palette (UK driving-licence convention) used by the Driving Licence credential.</summary>
    LicencePink
}
