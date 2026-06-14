// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Endpoints;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BpAction = Sorcha.Blueprint.Models.Action;
using Participant = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Catalogue;

/// <summary>
/// Feature B (154) — a published service is citizen-startable when its first action's sender
/// participant is open (no hard-coded wallet), so a citizen can initiate it.
/// </summary>
public sealed class CatalogueStartableTests
{
    private static BlueprintModel Bp(BpAction[] actions, Participant[] participants) =>
        new() { Actions = [.. actions], Participants = [.. participants] };

    [Fact]
    public void OpenFirstActionSender_IsStartable()
    {
        var bp = Bp(
            [new BpAction { Id = 1, Sender = "applicant" }],
            [new Participant { Id = "applicant", WalletAddress = null }]);

        CatalogueEndpoints.IsCitizenStartable(bp).Should().BeTrue();
    }

    [Fact]
    public void HardCodedFirstActionSender_IsNotStartable()
    {
        var bp = Bp(
            [new BpAction { Id = 1, Sender = "analyst" }],
            [new Participant { Id = "analyst", WalletAddress = "ws1qanalyst" }]);

        CatalogueEndpoints.IsCitizenStartable(bp).Should().BeFalse();
    }

    [Fact]
    public void UsesLowestIdAction_AsFirst()
    {
        var bp = Bp(
            [new BpAction { Id = 2, Sender = "analyst" }, new BpAction { Id = 1, Sender = "applicant" }],
            [new Participant { Id = "applicant", WalletAddress = null },
             new Participant { Id = "analyst", WalletAddress = "ws1qanalyst" }]);

        CatalogueEndpoints.IsCitizenStartable(bp).Should().BeTrue("the lowest-Id action is the first step");
    }

    [Fact]
    public void NoActions_IsNotStartable() =>
        CatalogueEndpoints.IsCitizenStartable(Bp([], [])).Should().BeFalse();

    [Fact]
    public void SenderParticipantMissing_IsNotStartable()
    {
        var bp = Bp([new BpAction { Id = 1, Sender = "ghost" }], [new Participant { Id = "other" }]);
        CatalogueEndpoints.IsCitizenStartable(bp).Should().BeFalse();
    }
}
