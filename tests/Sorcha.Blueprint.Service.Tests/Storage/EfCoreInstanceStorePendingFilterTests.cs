// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Tests.Storage;

/// <summary>
/// The pending-actions sender-filter (<see cref="EfCoreInstanceStore.IsActionForWallet"/>): a wallet
/// that merely participates in an instance must NOT be shown an action whose Sender is bound to a
/// DIFFERENT participant's wallet (the "citizen sees the analyst's Action 2" bug). It must still see
/// its own actions, must not hide ambiguous open/late-bound actions, and must fail open when the
/// blueprint can't be resolved.
/// </summary>
public class EfCoreInstanceStorePendingFilterTests
{
    private const string AnalystWallet = "ws-analyst";
    private const string CitizenWallet = "ws-citizen";

    /// <summary>AssuredIdentity-shaped blueprint: action 1 sender=applicant, action 2 sender=analyst.</summary>
    private static IActionResolverService Resolver()
    {
        var blueprint = new Sorcha.Blueprint.Models.Blueprint
        {
            Id = "bp-1",
            Actions =
            [
                new Sorcha.Blueprint.Models.Action { Id = 1, Sender = "applicant", IsStartingAction = true },
                new Sorcha.Blueprint.Models.Action { Id = 2, Sender = "analyst" },
                new Sorcha.Blueprint.Models.Action { Id = 3, Sender = "" }, // no sender declared
            ],
        };
        var mock = new Mock<IActionResolverService>();
        mock.Setup(r => r.GetActionDefinition(It.IsAny<Sorcha.Blueprint.Models.Blueprint>(), It.IsAny<string>()))
            .Returns((Sorcha.Blueprint.Models.Blueprint bp, string id) =>
                bp.Actions.FirstOrDefault(a => a.Id.ToString() == id));
        return mock.Object;
    }

    private static Sorcha.Blueprint.Models.Blueprint Blueprint() => new()
    {
        Id = "bp-1",
        Actions =
        [
            new Sorcha.Blueprint.Models.Action { Id = 1, Sender = "applicant", IsStartingAction = true },
            new Sorcha.Blueprint.Models.Action { Id = 2, Sender = "analyst" },
            new Sorcha.Blueprint.Models.Action { Id = 3, Sender = "" },
        ],
    };

    /// <summary>Instance at action 2 with both the applicant and the (pre-baked) analyst bound.</summary>
    private static Instance InstanceAtAction2() => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "tenant-1",
        CurrentActionIds = [2],
        ParticipantWallets = { ["applicant"] = CitizenWallet, ["analyst"] = AnalystWallet },
    };

    [Fact]
    public void AnalystSeesOwnAction()
    {
        EfCoreInstanceStore.IsActionForWallet(Blueprint(), Resolver(), InstanceAtAction2(), 2, AnalystWallet)
            .Should().BeTrue("the analyst is the Sender of action 2");
    }

    [Fact]
    public void CitizenDoesNotSeeAnalystAction()
    {
        // The bug: the citizen participates in the instance, so the unfiltered query surfaced the
        // analyst's action to them. The filter must exclude it.
        EfCoreInstanceStore.IsActionForWallet(Blueprint(), Resolver(), InstanceAtAction2(), 2, CitizenWallet)
            .Should().BeFalse("action 2's Sender (analyst) is bound to a different wallet");
    }

    [Fact]
    public void UnboundSenderIsNotHidden()
    {
        // Action 2's sender 'analyst' is not bound to any wallet yet (open / late-bound) — ambiguous,
        // so it must not be hidden from a participant who might be the one to act.
        var instance = new Instance
        {
            Id = "inst-2", BlueprintId = "bp-1", BlueprintVersion = 1, RegisterId = "reg-1",
            TenantId = "tenant-1", CurrentActionIds = [2],
            ParticipantWallets = { ["applicant"] = CitizenWallet }, // analyst not bound
        };
        EfCoreInstanceStore.IsActionForWallet(Blueprint(), Resolver(), instance, 2, CitizenWallet)
            .Should().BeTrue("an unbound sender is ambiguous and must not be hidden");
    }

    [Fact]
    public void ActionWithNoDeclaredSenderIsInclusive()
    {
        EfCoreInstanceStore.IsActionForWallet(Blueprint(), Resolver(), InstanceAtAction2(), 3, CitizenWallet)
            .Should().BeTrue("action 3 declares no Sender — fail open");
    }

    [Fact]
    public void NullBlueprintFailsOpen()
    {
        EfCoreInstanceStore.IsActionForWallet(null, Resolver(), InstanceAtAction2(), 2, CitizenWallet)
            .Should().BeTrue("a resolution failure must never hide a legitimate action");
    }
}
