// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

using Xunit;

using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// #1672 — the producer half: a rejection transaction must carry its discriminator on the METADATA,
/// which is the only copy that reaches the sealed ledger and therefore the projection.
/// </summary>
/// <remarks>
/// <para>
/// The builder put <c>type = "rejection"</c> on the payload only. The Validator's classifier reads
/// the payload too, so it recognised rejections and everything looked fine — while
/// <c>ToTransactionSubmission</c>'s whitelist, which copies <c>Metadata["type"]</c> onto the sealed
/// <c>TrackingData</c>, had nothing to copy. The projection fold therefore never learned the
/// transaction was a rejection and recorded rejected instances as Completed.
/// </para>
/// <para>
/// The consumer-side tests pass a hand-built <c>TrackingData</c>, so they cannot see this: a
/// mutation removing the metadata copy survived them both. These tests drive the real builder and
/// the real whitelist, which is where the join actually lives.
/// </para>
/// </remarks>
public class RejectionDiscriminatorReachesTheLedgerTests
{
    private static async Task<BuiltTransaction> BuildRejectionAsync()
    {
        var service = new Mock<ITransactionBuilderService>().Object;

        var blueprint = new BlueprintModel { Id = "bp-1", Title = "Test" };
        var action = new ActionModel { Id = 2, Title = "Review" };
        var instance = new Instance
        {
            Id = "inst-1",
            BlueprintId = "bp-1",
            RegisterId = "reg-1",
            BlueprintVersion = 1,
            TenantId = "tenant",
            State = InstanceState.Active,
        };

        return await service.BuildRejectionTransactionAsync(
            blueprint, instance, action,
            new Dictionary<string, object> { ["rejectionReason"] = "not acceptable" },
            previousTransactionId: "tx-prev");
    }

    [Fact]
    public async Task TheBuilderStampsTheDiscriminatorOnTheMetadata_NotOnlyThePayload()
    {
        var built = await BuildRejectionAsync();

        built.Metadata.Should().ContainKey("type",
            "the metadata copy is the only one ToTransactionSubmission can carry to the ledger");
        built.Metadata["type"].ToString().Should().Be("rejection");
    }

    [Fact]
    public async Task TheWhitelistCarriesItOntoTheSubmission()
    {
        // The whitelist is opt-in per key: a key nobody copies is silently dropped. This is the
        // step that was missing end to end.
        var built = await BuildRejectionAsync();

        var submission = built.ToTransactionSubmission(new Sorcha.ServiceClients.Wallet.WalletSignResult
        {
            Signature = new byte[64],
            PublicKey = new byte[32],
            SignedBy = "ws-rejector",
            Algorithm = "ED25519",
        });

        submission.Metadata.Should().ContainKey("type");
        submission.Metadata!["type"].Should().Be("rejection");
    }

    [Fact]
    public async Task ThePayloadAndTheMetadataAgree()
    {
        // The Validator classifies from the payload and the fold from the metadata. Two literals
        // that can drift apart is how one of them recognised a rejection and the other did not,
        // so both come from the same constant.
        var built = await BuildRejectionAsync();
        var payload = System.Text.Encoding.UTF8.GetString(built.TransactionData);

        payload.Should().Contain("\"type\":\"rejection\"");
        built.Metadata["type"].ToString().Should().Be("rejection");
    }
}
