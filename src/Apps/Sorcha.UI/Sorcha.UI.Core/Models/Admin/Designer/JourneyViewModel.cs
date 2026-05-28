// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Read-only, derived projection of a Blueprint for the F142 "Understand" canvas — a
/// plain-language, role-labelled journey of the service in sequence (FR-006/FR-007/FR-009).
/// This model is never authored or persisted; it is produced on demand by
/// <c>Sorcha.UI.Core.Services.Designer.JourneyViewMapper</c> from the current Blueprint.
/// </summary>
/// <param name="Steps">One <see cref="JourneyStep"/> per Blueprint Action, in flow order.</param>
public sealed record JourneyViewModel(IReadOnlyList<JourneyStep> Steps)
{
    /// <summary>An empty journey (no Blueprint, or a Blueprint with no actions).</summary>
    public static JourneyViewModel Empty { get; } = new([]);
}

/// <summary>
/// A single step in the derived journey, corresponding to one Blueprint Action.
/// </summary>
/// <param name="Role">The participant acting at this step (display name + stable colour).</param>
/// <param name="Title">The Action title (e.g. "Apply", "Review", "Approve").</param>
/// <param name="PlainSummary">A plain-language, factual one-line summary of what happens here.</param>
/// <param name="Badges">
/// Credential affordances surfaced on the step — "Must prove …" for each
/// <c>credentialRequirements</c> entry and "Issues …" when a <c>credentialIssuanceConfig</c> is present.
/// </param>
/// <param name="Detail">The expandable step detail revealed when the step is selected (FR-009).</param>
public sealed record JourneyStep(
    JourneyRole Role,
    string Title,
    string PlainSummary,
    IReadOnlyList<JourneyBadge> Badges,
    JourneyStepDetail Detail);

/// <summary>
/// The participant role for a journey step — a display name plus a stable colour so the UI
/// can colour-band a participant consistently across every step they act on.
/// </summary>
/// <param name="ParticipantId">The Blueprint participant id (stable key).</param>
/// <param name="Name">The participant's friendly display name.</param>
/// <param name="Index">Zero-based participant index used to derive the colour.</param>
/// <param name="Colour">The hex colour assigned to this participant.</param>
public sealed record JourneyRole(string ParticipantId, string Name, int Index, string Colour);

/// <summary>
/// Distinguishes a credential-prerequisite badge from a credential-issuance badge so the UI
/// (T016) can render "Must prove …" and "Issues …" distinctly.
/// </summary>
public enum JourneyBadgeKind
{
    /// <summary>A credential the participant must present to execute the step (from <c>credentialRequirements</c>).</summary>
    MustProve = 0,

    /// <summary>A credential the step issues on execution (from <c>credentialIssuanceConfig</c>).</summary>
    Issues = 1
}

/// <summary>
/// A credential affordance on a journey step.
/// </summary>
/// <param name="Kind">Whether the credential is required (Must prove) or issued (Issues).</param>
/// <param name="CredentialType">The credential type identifier (e.g. "IdentityAttestation").</param>
public sealed record JourneyBadge(JourneyBadgeKind Kind, string CredentialType);

/// <summary>
/// The detail revealed when a journey step is selected (FR-009): what the participant sees
/// (the Disclosure), the decision they make, and the form they fill.
/// </summary>
/// <param name="ActionId">The underlying Blueprint Action id.</param>
/// <param name="DisclosureSummary">A plain-language summary of what data this step's actor can see.</param>
/// <param name="Decision">A plain-language summary of the decision/branching the participant makes, if any.</param>
/// <param name="FormReference">A stable reference to the form the participant fills (e.g. "action-1").</param>
public sealed record JourneyStepDetail(
    int ActionId,
    string DisclosureSummary,
    string? Decision,
    string FormReference);
