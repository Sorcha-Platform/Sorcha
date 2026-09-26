// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

using Xunit;

using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// #1576 — a rejection transaction carries the definition pin of the instance it rejects, so the
/// projector folds it against that definition instead of taking the pre-Feature-194 fallback and
/// counting a <c>pin_fallback</c> for what is an ordinary refusal.
/// </summary>
/// <remarks>
/// A rejection carries no <c>RoutingDecision</c>, which is where every other action's pin rides.
/// The pin therefore rides <c>TrackingData</c>, which is outside the signature — so the fold may use
/// it only to CONFIRM a pin the instance already holds, never to establish one. Forging it can then
/// only get the forger's own rejection refused.
/// </remarks>
public class RejectionCarriesItsDefinitionPinTests
{
    private const string Pin = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OtherPin = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static Task<BuiltTransaction> BuildRejectionAsync(string pin) =>
        new Mock<ITransactionBuilderService>().Object.BuildRejectionTransactionAsync(
            new BlueprintModel { Id = "bp-1", Title = "Test" },
            new Instance
            {
                Id = "inst-1",
                BlueprintId = "bp-1",
                RegisterId = "reg-1",
                BlueprintVersion = 1,
                TenantId = "tenant",
                State = InstanceState.Active,
                BlueprintDefinitionTxId = pin,
            },
            new ActionModel { Id = 2, Title = "Review" },
            new Dictionary<string, object> { ["rejectionReason"] = "not acceptable" },
            previousTransactionId: "tx-prev");

    private static TransactionSubmissionView Submit(BuiltTransaction built) =>
        new(built.ToTransactionSubmission(new Sorcha.ServiceClients.Wallet.WalletSignResult
        {
            Signature = new byte[64],
            PublicKey = new byte[32],
            SignedBy = "ws-rejector",
            Algorithm = "ED25519",
        }).Metadata ?? new Dictionary<string, string>());

    private sealed record TransactionSubmissionView(IReadOnlyDictionary<string, string> Metadata);

    private static TransactionModel SealedTx(Dictionary<string, string> tracking) => new()
    {
        TxId = "tx-rejection",
        PrevTxId = "tx-prev",
        SenderWallet = "ws-rejector",
        MetaData = new TransactionMetaData
        {
            BlueprintId = "bp-1",
            InstanceId = "inst-1",
            ActionId = 2,
            TransactionType = TransactionType.Action,
            TrackingData = tracking,
        },
    };

    private static Task<InstanceProjectionResolver.ResolvedProjection?> ResolveAsync(TransactionModel tx) =>
        InstanceProjectionResolver.ResolveAsync(
            tx, new Mock<IActionResolverService>().Object, NullLogger.Instance, CancellationToken.None);

    [Fact]
    public async Task TheBuiltRejectionCarriesTheInstancePinOntoTheSubmission()
    {
        var submission = Submit(await BuildRejectionAsync(Pin));

        submission.Metadata.Should().ContainKey("blueprintDefinitionTxId",
            "the submission metadata is a whitelist, and it is what the validator seals as TrackingData");
        submission.Metadata["blueprintDefinitionTxId"].Should().Be(Pin);
    }

    [Fact]
    public async Task AnUnpinnedInstanceEmitsNoPin_RatherThanAnEmptyOne()
    {
        var submission = Submit(await BuildRejectionAsync(string.Empty));

        submission.Metadata.Should().NotContainKey("blueprintDefinitionTxId");
    }

    [Fact]
    public async Task TheResolverReadsTheRejectionsPin()
    {
        var resolved = await ResolveAsync(SealedTx(new()
        {
            ["type"] = "rejection",
            ["blueprintDefinitionTxId"] = Pin,
        }));

        resolved.Should().NotBeNull();
        resolved!.Tx.IsRejection.Should().BeTrue();
        resolved.Tx.BlueprintDefinitionTxId.Should().Be(Pin);
    }

    [Fact]
    public async Task TheResolverIgnoresAnUnsignedPinOnAnythingButARejection()
    {
        // An ordinary action's pin rides its signed RoutingDecision. A TrackingData copy on one is
        // not something this codebase writes, and reading it would let an unsigned field stand in
        // for the signed one.
        var resolved = await ResolveAsync(SealedTx(new()
        {
            ["blueprintDefinitionTxId"] = Pin,
        }));

        resolved.Should().NotBeNull();
        resolved!.Tx.BlueprintDefinitionTxId.Should().BeNull();
    }

    private static Instance PinnedInstance(string pin) => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        RegisterId = "reg-1",
        BlueprintVersion = 1,
        TenantId = "tenant",
        State = InstanceState.Active,
        BlueprintDefinitionTxId = pin,
        CurrentActionIds = [2],
    };

    private static ProjectedTransaction Rejection(string? pin) => new(
        TxId: "tx-rejection",
        PreviousTransactionId: "tx-prev",
        CompletedActionId: 2,
        NextActionIds: [],
        ParticipantBindings: new Dictionary<string, string>(),
        IsRejection: true,
        BlueprintDefinitionTxId: pin);

    [Fact]
    public void ARejectionMatchingTheInstancePinFolds()
    {
        InstanceProjection.Apply(PinnedInstance(Pin), Rejection(Pin))
            .Should().Be(FoldOutcome.Advanced);
    }

    [Fact]
    public void ARejectionClaimingAnotherDefinitionIsRefused()
    {
        InstanceProjection.Apply(PinnedInstance(Pin), Rejection(OtherPin))
            .Should().Be(FoldOutcome.RefusedForeignDefinition);
    }

    [Fact]
    public void ARejectionsUnsignedPinNeverEstablishesTheInstancePin()
    {
        var instance = PinnedInstance(string.Empty);

        InstanceProjection.Apply(instance, Rejection(Pin)).Should().Be(FoldOutcome.Advanced);

        instance.BlueprintDefinitionTxId.Should().BeEmpty(
            "the rejection's pin is outside the signature, so it may confirm a pin but not create one");
    }
}
