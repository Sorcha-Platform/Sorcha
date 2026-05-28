// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Feature 142 US4 (FR-010 / FR-012) — turns a stable "directed-build" starter id chosen by the
/// administrator into a deterministic seed <see cref="BlueprintModel"/>. The mapping is plain-
/// language → constructs: the administrator picks a recognisable shape ("apply for a grant",
/// "apply for a permit / licence", "certify, then apply") and this helper produces the right
/// underlying constructs (open starting action, credential prerequisite, credential issuance)
/// without exposing jargon to the UI.
/// </summary>
/// <remarks>
/// The set of known starter ids is closed and small on purpose — these are the affordances the
/// AiDesignerPane chip row offers on a fresh designer load. Each shape is intentionally minimal:
/// the AI takes it from here, refining the journey as the administrator answers follow-up
/// questions. Three constants are public so the chat orchestration, UI and tests share the same
/// vocabulary.
/// </remarks>
public sealed class DirectedBuildStarter
{
    /// <summary>Stable starter id: "apply for a grant".</summary>
    public const string Grant = "grant";

    /// <summary>Stable starter id: "apply for a permit / licence".</summary>
    public const string Permit = "permit";

    /// <summary>Stable starter id: "certify, then apply" — credential-gated open submission.</summary>
    public const string CertifyThenApply = "certify-then-apply";

    /// <summary>The closed set of recognised directed-build ids.</summary>
    public static IReadOnlyList<string> KnownStarterIds { get; } =
        [Grant, Permit, CertifyThenApply];

    /// <summary>
    /// Returns a deterministic seed <see cref="BlueprintModel"/> for the given <paramref name="starterId"/>,
    /// or <c>null</c> when the id is unrecognised. Calling with the same id always produces structurally
    /// identical output (same titles, participant ids, action shapes), so tests and the UI can rely on it.
    /// </summary>
    public BlueprintModel? TryCreateSeed(string? starterId)
    {
        if (string.IsNullOrWhiteSpace(starterId)) { return null; }

        return starterId switch
        {
            Grant => BuildGrant(),
            Permit => BuildPermit(),
            CertifyThenApply => BuildCertifyThenApply(),
            _ => null,
        };
    }

    private static BlueprintModel BuildGrant() => new()
    {
        Id = string.Empty, // assigned by the store on save
        Title = "Grant Application",
        Description = "An applicant applies for a grant; a reviewer assesses the application and decides.",
        Version = 1,
        Participants =
        [
            new Participant { Id = "applicant", Name = "Applicant", WalletAddress = null },
            new Participant { Id = "reviewer", Name = "Reviewer" },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Apply for grant",
                Description = "The applicant submits their grant application.",
                Sender = "applicant",
                IsStartingAction = true,
                Routes = [new Sorcha.Blueprint.Models.Route { Id = "to-review", NextActionIds = [2], IsDefault = true }],
            },
            new ActionModel
            {
                Id = 2,
                Title = "Review application",
                Description = "The reviewer assesses the application and records a decision.",
                Sender = "reviewer",
                Routes = [new Sorcha.Blueprint.Models.Route { Id = "done", NextActionIds = [], IsDefault = true }],
            },
        ],
    };

    private static BlueprintModel BuildPermit() => new()
    {
        Id = string.Empty,
        Title = "Permit / Licence Application",
        Description = "An applicant applies for a permit; an officer assesses it and issues the permit on approval.",
        Version = 1,
        Participants =
        [
            new Participant { Id = "applicant", Name = "Applicant", WalletAddress = null },
            new Participant { Id = "officer", Name = "Case Officer" },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Apply for permit",
                Description = "The applicant submits the permit application.",
                Sender = "applicant",
                IsStartingAction = true,
                Routes = [new Sorcha.Blueprint.Models.Route { Id = "to-decide", NextActionIds = [2], IsDefault = true }],
            },
            new ActionModel
            {
                Id = 2,
                Title = "Decide and issue permit",
                Description = "The case officer reviews the application and issues a permit on approval.",
                Sender = "officer",
                CredentialIssuanceConfig = new CredentialIssuanceConfig
                {
                    CredentialType = "Permit",
                },
                Routes = [new Sorcha.Blueprint.Models.Route { Id = "done", NextActionIds = [], IsDefault = true }],
            },
        ],
    };

    private static BlueprintModel BuildCertifyThenApply() => new()
    {
        Id = string.Empty,
        Title = "Certified Applicant",
        Description = "The applicant must already hold a certification before they may apply; a reviewer then assesses the application.",
        Version = 1,
        Participants =
        [
            new Participant { Id = "applicant", Name = "Applicant", WalletAddress = null },
            new Participant { Id = "reviewer", Name = "Reviewer" },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Submit application",
                Description = "The applicant submits the application. The applicant must already hold the required certification.",
                Sender = "applicant",
                IsStartingAction = true,
                CredentialRequirements =
                [
                    new CredentialRequirement { Type = "Certification" },
                ],
                Routes = [new Sorcha.Blueprint.Models.Route { Id = "to-review", NextActionIds = [2], IsDefault = true }],
            },
            new ActionModel
            {
                Id = 2,
                Title = "Review application",
                Description = "The reviewer assesses the application and records a decision.",
                Sender = "reviewer",
                Routes = [new Sorcha.Blueprint.Models.Route { Id = "done", NextActionIds = [], IsDefault = true }],
            },
        ],
    };
}
