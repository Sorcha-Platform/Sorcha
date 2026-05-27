// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Runtime context for a review-card render — the action's current lifecycle
/// state as seen from the hosting component. Drives watermark and footer-action
/// selection in <see cref="IdCardLayoutConfig"/>. Feature 107 Assured Identity v1.
/// </summary>
public enum ActionRuntimeState
{
    /// <summary>Citizen is on their own pre-submission review page (Draft + Edit/Submit).</summary>
    CitizenDraft,

    /// <summary>Assessor is viewing a pending application (Pending + Approve/Reject).</summary>
    AssessorPending,

    /// <summary>Citizen is viewing the issued credential in their wallet (Issued, no actions).</summary>
    IssuedCredential,

    /// <summary>Citizen is previewing a not-yet-issued credential offer (no watermark, no actions).</summary>
    CredentialPreview
}

/// <summary>
/// Watermark treatment rendered over the top-right of the ID card layout.
/// </summary>
public enum IdCardWatermark
{
    None,
    Draft,
    Pending,
    Issued
}

/// <summary>
/// A titled group of fields on the review card, mapped back to its originating
/// wizard page index so the Edit control can navigate the user home.
/// </summary>
/// <param name="Title">Section heading (e.g. "Your name", "Address").</param>
/// <param name="OriginatingPageIndex">Zero-based wizard page index to jump back to.</param>
/// <param name="FieldPointers">JSON Pointers into <see cref="FormContext.FormData"/>.</param>
public sealed record IdCardSection(
    string Title,
    int OriginatingPageIndex,
    IReadOnlyList<string> FieldPointers);

/// <summary>
/// Everything <c>IdCardLayout.razor</c> needs to render a single review card.
/// Built by <see cref="Sorcha.UI.Core.Services.Forms.ReviewSummaryDataSource"/>
/// from a parsed <see cref="XReviewExtension"/> and the live form context.
/// </summary>
public sealed record IdCardLayoutConfig
{
    public required string IssuerName { get; init; }
    public required string CredentialName { get; init; }
    public required XReviewColourTheme ColourTheme { get; init; }
    public required IdCardWatermark Watermark { get; init; }
    public required IReadOnlyDictionary<string, object?> FieldValues { get; init; }
    public required IReadOnlyList<IdCardSection> Sections { get; init; }
    public required bool Editable { get; init; }
}
