// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// #1658 — which of the caller's wallets may submit an action. The rule has to agree with what
/// <c>ActionExecutionService.ExecuteAsync</c> will accept, or an agent is told a wallet the
/// execute path then refuses: a hard-coded blueprint participant wallet is matched strictly
/// (step 4c), and an instance binding is immutable once made (step 4d).
/// </summary>
public sealed class SenderWalletResolverTests
{
    private const string Mine = "ws1qmine000000000000000000000000000000000";
    private const string AlsoMine = "ws1qalso000000000000000000000000000000000";
    private const string Theirs = "ws1qtheirs0000000000000000000000000000000";

    private static (BlueprintModel Blueprint, Sorcha.Blueprint.Models.Action Action) Definition(
        string? hardcodedSenderWallet = null)
    {
        var action = new Sorcha.Blueprint.Models.Action { Id = 2, Title = "Review", Sender = "reviewer" };
        var blueprint = new BlueprintModel
        {
            Id = "bp-1", Title = "Two party", Description = "desc-desc",
            Participants =
            [
                new Participant { Id = "applicant", Name = "Applicant" },
                new Participant { Id = "reviewer", Name = "Reviewer", WalletAddress = hardcodedSenderWallet },
            ],
            Actions = [action],
        };
        return (blueprint, action);
    }

    private static Instance InstanceWith(Dictionary<string, string> bindings) => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "default",
        CurrentActionIds = [2],
        ParticipantWallets = bindings,
    };

    [Fact]
    public void Resolve_SenderBoundOnInstanceToCallersWallet_ResolvesThatWallet()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["reviewer"] = AlsoMine });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine, AlsoMine]);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(AlsoMine, "an instance binding is immutable, so it is the only wallet execute accepts");
    }

    [Fact]
    public void Resolve_SenderBoundOnInstanceToSomeoneElse_IsNotYours()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["reviewer"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine]);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.NotYours);
        result.SenderWallet.Should().BeNull();
    }

    [Fact]
    public void Resolve_HardcodedBlueprintWalletHeldByCaller_ResolvesIt()
    {
        var (blueprint, action) = Definition(hardcodedSenderWallet: Mine);
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [AlsoMine, Mine]);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine);
    }

    [Fact]
    public void Resolve_HardcodedBlueprintWalletOverridesAConflictingInstanceBinding()
    {
        // Execute step 4c refuses any wallet other than the hard-coded one, whatever the instance says.
        var (blueprint, action) = Definition(hardcodedSenderWallet: Theirs);
        var instance = InstanceWith(new() { ["reviewer"] = Mine });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine]);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.NotYours);
    }

    [Fact]
    public void Resolve_UnboundSenderAndOneCallerWallet_ResolvesIt()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine]);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine);
    }

    [Fact]
    public void Resolve_UnboundSenderAndSeveralCallerWallets_IsAmbiguousWithCandidates()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine, AlsoMine]);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Ambiguous);
        result.SenderWallet.Should().BeNull("guessing one of several wallets would bind the wrong one immutably");
        result.CandidateWallets.Should().BeEquivalentTo([Mine, AlsoMine]);
    }

    [Fact]
    public void Resolve_CallerHoldsNoWallet_IsNoWallet()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, []);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.NoWallet);
        result.SenderWallet.Should().BeNull();
    }

    [Fact]
    public void Resolve_BindingMatchIsCaseInsensitive_LikeExecute()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["reviewer"] = Mine.ToUpperInvariant() });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine]);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine.ToUpperInvariant(), "the bound spelling is what execute compares against");
    }

    [Fact]
    public void Resolve_NeverOffersAWalletTheCallerDoesNotHold()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["applicant"] = Theirs, ["reviewer"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine]);

        result.CandidateWallets.Should().NotContain(Theirs);
        result.SenderWallet.Should().NotBe(Theirs);
    }

    [Fact]
    public void SenderWalletStatus_GoesOnTheWireAsItsName()
    {
        // Blueprint Service configures no JSON enum converter (CLAUDE.md pattern 25), so without an
        // attribute on the type this would be a bare integer that an agent cannot read.
        var json = JsonSerializer.Serialize(SenderWalletStatus.NotYours, JsonSerializerOptions.Web);

        json.Should().Be("\"notYours\"");
    }
}
