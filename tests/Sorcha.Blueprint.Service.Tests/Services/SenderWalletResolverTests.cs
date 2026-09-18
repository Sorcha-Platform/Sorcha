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
        string? hardcodedSenderWallet = null,
        bool isStartingAction = false)
    {
        var action = new Sorcha.Blueprint.Models.Action
        {
            Id = 2, Title = "Review", Sender = "reviewer", IsStartingAction = isStartingAction,
        };
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

    private static IReadOnlyList<string> Published(params string[] addresses) => addresses;

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

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine, AlsoMine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(AlsoMine, "an instance binding is immutable, so it is the only wallet execute accepts");
    }

    [Fact]
    public void Resolve_SenderBoundOnInstanceToSomeoneElse_IsNotYours()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["reviewer"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.NotYours);
        result.SenderWallet.Should().BeNull();
    }

    [Fact]
    public void Resolve_HardcodedBlueprintWalletHeldByCaller_ResolvesIt()
    {
        var (blueprint, action) = Definition(hardcodedSenderWallet: Mine);
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [AlsoMine, Mine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine);
    }

    [Fact]
    public void Resolve_HardcodedBlueprintWalletOverridesAConflictingInstanceBinding()
    {
        // Execute step 4c refuses any wallet other than the hard-coded one, whatever the instance says.
        var (blueprint, action) = Definition(hardcodedSenderWallet: Theirs);
        var instance = InstanceWith(new() { ["reviewer"] = Mine });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.NotYours);
    }

    [Fact]
    public void Resolve_StartingActionUnboundSenderAndOneCallerWallet_ResolvesIt()
    {
        // Only a STARTING action late-binds (Feature 103), so this is the one case where an unbound
        // sender may be offered the caller's own wallet.
        var (blueprint, action) = Definition(isStartingAction: true);
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine);
    }

    [Fact]
    public void Resolve_LaterActionUnboundSender_IsNotSubmittable_NotTheCallersOwnWallet()
    {
        // #1664, found live in cold-start run #4: offering the caller's wallet here named a wallet the
        // validator ALWAYS refuses (VAL_BP_002), after a 202, with no audit entry and no way to see why.
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.AwaitingParticipantRecord);
        result.SenderWallet.Should().BeNull();
        result.CandidateWallets.Should().BeEmpty();
        result.UnboundParticipantId.Should().Be("reviewer");
    }

    [Fact]
    public void Resolve_UnboundSenderAndSeveralCallerWallets_IsAmbiguousWithCandidates()
    {
        var (blueprint, action) = Definition(isStartingAction: true);
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine, AlsoMine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Ambiguous);
        result.SenderWallet.Should().BeNull("guessing one of several wallets would bind the wrong one immutably");
        result.CandidateWallets.Should().BeEquivalentTo([Mine, AlsoMine]);
    }

    [Fact]
    public void Resolve_CallerHoldsNoWallet_IsNoWallet()
    {
        var (blueprint, action) = Definition(isStartingAction: true);
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.NoWallet);
        result.SenderWallet.Should().BeNull();
    }

    [Fact]
    public void Resolve_BindingMatchIsCaseInsensitive_LikeExecute()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["reviewer"] = Mine.ToUpperInvariant() });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine.ToUpperInvariant(), "the bound spelling is what execute compares against");
    }

    [Fact]
    public void Resolve_NeverOffersAWalletTheCallerDoesNotHold()
    {
        var (blueprint, action) = Definition(isStartingAction: true);
        var instance = InstanceWith(new() { ["applicant"] = Theirs, ["reviewer"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: null);

        result.CandidateWallets.Should().NotContain(Theirs);
        result.SenderWallet.Should().NotBe(Theirs);
    }

    // ---- published participant record: validator VAL_BP_002 Tier 2 ----

    [Fact]
    public void Resolve_PublishedRecordAddressHeldByCaller_ResolvesIt()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(
            blueprint, action, instance, [Mine, AlsoMine], Published(Theirs, AlsoMine));

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(AlsoMine, "the signer must be one of the published addresses");
    }

    [Fact]
    public void Resolve_PublishedRecordHeldBySomeoneElse_IsNotYours()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["applicant"] = Theirs });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], Published(Theirs));

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.NotYours);
        result.SenderWallet.Should().BeNull();
    }

    [Fact]
    public void Resolve_PublishedRecordWins_OverAnInstanceBinding()
    {
        // The validator checks the published record BEFORE the chain-derived binding and refuses a
        // signer that is not a published address, so preferring the binding would offer a doomed wallet.
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["reviewer"] = Mine });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine, AlsoMine], Published(AlsoMine));

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(AlsoMine);
    }

    [Fact]
    public void Resolve_HardcodedBlueprintWallet_WinsOverAPublishedRecord()
    {
        var (blueprint, action) = Definition(hardcodedSenderWallet: Mine);
        var instance = InstanceWith(new());

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine, AlsoMine], Published(AlsoMine));

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine);
    }

    [Fact]
    public void Resolve_LaterActionBoundByAnEarlierActionInThisInstance_ResolvesThatWallet()
    {
        // Tier 3: the same role already signed in this instance, so that wallet is the binding.
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new() { ["reviewer"] = Mine });

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: null);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.Resolved);
        result.SenderWallet.Should().Be(Mine);
    }

    [Fact]
    public void Resolve_AwaitingParticipantRecord_NamesTheRoleSoTheCallerKnowsWhatToPublish()
    {
        var (blueprint, action) = Definition();
        var instance = InstanceWith(new());

        var result = SenderWalletResolver.Resolve(blueprint, action, instance, [Mine], publishedAddresses: []);

        result.SenderWalletStatus.Should().Be(SenderWalletStatus.AwaitingParticipantRecord);
        result.UnboundParticipantId.Should().Be("reviewer");
    }

    [Fact]
    public void SenderWalletStatus_GoesOnTheWireAsItsName()
    {
        // Blueprint Service configures no JSON enum converter (CLAUDE.md pattern 25), so without an
        // attribute on the type this would be a bare integer that an agent cannot read.
        JsonSerializer.Serialize(SenderWalletStatus.NotYours, JsonSerializerOptions.Web).Should().Be("\"notYours\"");
        JsonSerializer.Serialize(SenderWalletStatus.AwaitingParticipantRecord, JsonSerializerOptions.Web)
            .Should().Be("\"awaitingParticipantRecord\"");
    }
}
