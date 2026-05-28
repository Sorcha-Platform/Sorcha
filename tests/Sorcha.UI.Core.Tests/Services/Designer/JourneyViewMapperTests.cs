// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Models.Designer;
using Sorcha.UI.Core.Services.Designer;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Services.Designer;

/// <summary>
/// Pure unit tests for <see cref="JourneyViewMapper"/> — no rendering, deterministic projection
/// of a <see cref="BlueprintModel"/> into the read-only Understand-canvas journey (F142 T007).
/// </summary>
public class JourneyViewMapperTests
{
    private static Participant ParticipantNamed(string id, string name, string? wallet = null) =>
        new() { Id = id, Name = name, WalletAddress = wallet };

    [Fact]
    public void Map_NullBlueprint_ReturnsEmptyJourney()
    {
        var journey = JourneyViewMapper.Map(null);

        journey.Steps.Should().BeEmpty();
    }

    [Fact]
    public void Map_SingleStartingAction_ProducesOneStep()
    {
        var bp = new BlueprintModel
        {
            Participants = [ParticipantNamed("applicant", "Applicant")],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1,
                    Title = "Apply",
                    Sender = "applicant",
                    IsStartingAction = true,
                    Disclosures = [new Disclosure("applicant", ["/*"])]
                }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        journey.Steps.Should().HaveCount(1);
        journey.Steps[0].Title.Should().Be("Apply");
        journey.Steps[0].Role.Name.Should().Be("Applicant");
    }

    [Fact]
    public void Map_StartingAction_RoleSummaryUsesApplies()
    {
        var bp = new BlueprintModel
        {
            Participants = [ParticipantNamed("applicant", "Applicant")],
            Actions =
            [
                new BlueprintAction { Id = 1, Title = "Apply", Sender = "applicant", IsStartingAction = true }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        journey.Steps[0].PlainSummary.Should().Contain("Applicant");
        journey.Steps[0].PlainSummary.Should().Contain("applies");
    }

    [Fact]
    public void Map_CredentialGatedStartingAction_YieldsMustProveBadge()
    {
        var bp = new BlueprintModel
        {
            Participants = [ParticipantNamed("applicant", "Applicant")],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1,
                    Title = "Apply",
                    Sender = "applicant",
                    IsStartingAction = true,
                    CredentialRequirements =
                    [
                        new CredentialRequirement { Type = "IdentityAttestation" }
                    ]
                }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        journey.Steps[0].Badges.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new JourneyBadge(JourneyBadgeKind.MustProve, "IdentityAttestation"));
    }

    [Fact]
    public void Map_ActionWithIssuanceConfig_YieldsIssuesBadge()
    {
        var bp = new BlueprintModel
        {
            Participants =
            [
                ParticipantNamed("applicant", "Applicant"),
                ParticipantNamed("assessor", "Assessor", "ws1assessor")
            ],
            Actions =
            [
                new BlueprintAction { Id = 1, Title = "Apply", Sender = "applicant", IsStartingAction = true },
                new BlueprintAction
                {
                    Id = 2,
                    Title = "Approve",
                    Sender = "assessor",
                    CredentialIssuanceConfig = new CredentialIssuanceConfig
                    {
                        CredentialType = "LicenseCredential",
                        RecipientParticipantId = "applicant",
                        ClaimMappings = [new ClaimMapping { ClaimName = "name", SourceField = "/name" }]
                    }
                }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        var approveStep = journey.Steps.Single(s => s.Title == "Approve");
        approveStep.Badges.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new JourneyBadge(JourneyBadgeKind.Issues, "LicenseCredential"));
    }

    [Fact]
    public void Map_IssuanceStep_SummaryMentionsIssues()
    {
        var bp = new BlueprintModel
        {
            Participants = [ParticipantNamed("assessor", "Assessor", "ws1assessor")],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1,
                    Title = "Grant",
                    Sender = "assessor",
                    IsStartingAction = true,
                    CredentialIssuanceConfig = new CredentialIssuanceConfig
                    {
                        CredentialType = "LicenseCredential",
                        RecipientParticipantId = "assessor",
                        ClaimMappings = [new ClaimMapping { ClaimName = "name", SourceField = "/name" }]
                    }
                }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        journey.Steps[0].PlainSummary.Should().Contain("issues");
        journey.Steps[0].PlainSummary.Should().Contain("LicenseCredential");
    }

    [Fact]
    public void Map_StepsFollowRouteOrder_NotDeclarationOrder()
    {
        // Declared 1, 3, 2 but routes chain 1 -> 3 -> 2.
        var bp = new BlueprintModel
        {
            Participants =
            [
                ParticipantNamed("a", "Alpha"),
                ParticipantNamed("b", "Bravo", "ws1b"),
                ParticipantNamed("c", "Charlie", "ws1c")
            ],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1, Title = "First", Sender = "a", IsStartingAction = true,
                    Routes = [new Route { Id = "r1", NextActionIds = [3] }]
                },
                new BlueprintAction
                {
                    Id = 3, Title = "Third", Sender = "c",
                    Routes = [new Route { Id = "r3", NextActionIds = [2] }]
                },
                new BlueprintAction { Id = 2, Title = "Second", Sender = "b" }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        journey.Steps.Select(s => s.Title).Should().Equal("First", "Third", "Second");
    }

    [Fact]
    public void Map_NoRoutes_FallsBackToActionIdOrder()
    {
        var bp = new BlueprintModel
        {
            Participants =
            [
                ParticipantNamed("a", "Alpha"),
                ParticipantNamed("b", "Bravo", "ws1b")
            ],
            Actions =
            [
                new BlueprintAction { Id = 2, Title = "Second", Sender = "b" },
                new BlueprintAction { Id = 1, Title = "First", Sender = "a", IsStartingAction = true }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        journey.Steps.Select(s => s.Title).Should().Equal("First", "Second");
    }

    [Fact]
    public void Map_ReviewerWithRoutes_SummaryMentionsReviews()
    {
        var bp = new BlueprintModel
        {
            Participants =
            [
                ParticipantNamed("a", "Applicant"),
                ParticipantNamed("r", "Reviewer", "ws1r")
            ],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1, Title = "Apply", Sender = "a", IsStartingAction = true,
                    Routes = [new Route { Id = "r1", NextActionIds = [2] }]
                },
                new BlueprintAction
                {
                    Id = 2, Title = "Review", Sender = "r",
                    Routes =
                    [
                        new Route { Id = "approve", NextActionIds = [] },
                        new Route { Id = "reject", NextActionIds = [], IsDefault = true }
                    ]
                }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        var reviewStep = journey.Steps.Single(s => s.Title == "Review");
        reviewStep.PlainSummary.Should().Contain("reviews");
    }

    [Fact]
    public void Map_PopulatesDetail_Disclosure_Decision_FormReference()
    {
        var bp = new BlueprintModel
        {
            Participants =
            [
                ParticipantNamed("a", "Applicant"),
                ParticipantNamed("r", "Reviewer", "ws1r")
            ],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1, Title = "Apply", Sender = "a", IsStartingAction = true,
                    Routes = [new Route { Id = "r1", NextActionIds = [2] }],
                    Disclosures =
                    [
                        new Disclosure("a", ["/name", "/dob"]),
                        new Disclosure("r", ["/*"])
                    ]
                },
                new BlueprintAction
                {
                    Id = 2, Title = "Review", Sender = "r",
                    Routes =
                    [
                        new Route { Id = "approve", NextActionIds = [], Description = "Approve the application" },
                        new Route { Id = "reject", NextActionIds = [], IsDefault = true }
                    ]
                }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        var applyStep = journey.Steps.Single(s => s.Title == "Apply");
        applyStep.Detail.DisclosureSummary.Should().NotBeNullOrWhiteSpace();
        applyStep.Detail.FormReference.Should().Be("action-1");
        applyStep.Detail.ActionId.Should().Be(1);

        var reviewStep = journey.Steps.Single(s => s.Title == "Review");
        reviewStep.Detail.Decision.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Map_MultipleCredentialRequirements_YieldsBadgePerRequirement()
    {
        var bp = new BlueprintModel
        {
            Participants = [ParticipantNamed("applicant", "Applicant")],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1, Title = "Apply", Sender = "applicant", IsStartingAction = true,
                    CredentialRequirements =
                    [
                        new CredentialRequirement { Type = "IdentityAttestation" },
                        new CredentialRequirement { Type = "AddressProof" }
                    ]
                }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        journey.Steps[0].Badges.Should().BeEquivalentTo(new[]
        {
            new JourneyBadge(JourneyBadgeKind.MustProve, "IdentityAttestation"),
            new JourneyBadge(JourneyBadgeKind.MustProve, "AddressProof")
        });
    }

    [Fact]
    public void Map_RoleColourIsStableAcrossSteps_ForSameParticipant()
    {
        var bp = new BlueprintModel
        {
            Participants =
            [
                ParticipantNamed("a", "Applicant"),
                ParticipantNamed("r", "Reviewer", "ws1r")
            ],
            Actions =
            [
                new BlueprintAction
                {
                    Id = 1, Title = "Apply", Sender = "a", IsStartingAction = true,
                    Routes = [new Route { Id = "r1", NextActionIds = [2] }]
                },
                new BlueprintAction
                {
                    Id = 2, Title = "Amend", Sender = "a",
                    Routes = [new Route { Id = "r2", NextActionIds = [3] }]
                },
                new BlueprintAction { Id = 3, Title = "Review", Sender = "r" }
            ]
        };

        var journey = JourneyViewMapper.Map(bp);

        var applyColour = journey.Steps.Single(s => s.Title == "Apply").Role.Colour;
        var amendColour = journey.Steps.Single(s => s.Title == "Amend").Role.Colour;
        var reviewColour = journey.Steps.Single(s => s.Title == "Review").Role.Colour;

        applyColour.Should().Be(amendColour);
        applyColour.Should().NotBe(reviewColour);
    }
}
