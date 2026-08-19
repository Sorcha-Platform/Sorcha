// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Regression tests for issue #1515 — governance activity burying the blueprint it is governed by.
/// </summary>
/// <remarks>
/// <para>
/// Every governance transaction on the system register carries
/// <c>MetaData.BlueprintId = "register-governance-v1"</c>, because it genuinely is an action
/// submission against that workflow. The blueprint lookup filtered on nothing but that field and
/// took the newest match, so the moment anyone governed the SSR, "the governance blueprint" started
/// resolving to a governance CONTROL TRANSACTION instead.
/// </para>
/// <para>
/// Nothing announced it. The control payload is valid JSON, so it deserialized into a blueprint
/// without complaint — just one with no actions — and the Validator refused the next governance
/// transaction with <c>VAL_SCHEMA_003: Action 1 not found in blueprint 'register-governance-v1'</c>,
/// an error naming a blueprint that was, in fact, perfectly correct. Because the Validator caches
/// what it resolves, the register kept working until the first cache miss, and then governance
/// stopped platform-wide, since the resolution is one global lookup on the SSR.
/// </para>
/// <para>
/// The fixtures below are built from the transactions observed on n1 on 2026-08-19, after the
/// F189 US4 ownership transfer bricked governance on a node that had been genesised clean hours
/// earlier: docket 2 is the real publication, dockets 5-14 the governance activity that buried it.
/// </para>
/// </remarks>
public class SystemRegisterBlueprintPollutionTests
{
    private const string GovernanceBlueprintId = "register-governance-v1";

    private readonly Mock<IRegisterRepository> _repository;
    private readonly SystemRegisterService _service;

    public SystemRegisterBlueprintPollutionTests()
    {
        _repository = new Mock<IRegisterRepository>();
        RegisterMockHelpers.StubTransactionsByTypeReadThrough(_repository);

        var events = new Mock<IEventPublisher>();
        var hash = new Mock<IHashProvider>();
        hash.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<Sorcha.Cryptography.Enums.HashType>()))
            .Returns(new byte[32]);

        _repository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Name = SystemRegisterConstants.SystemRegisterName,
                Height = 0,
                Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
            });

        _service = new SystemRegisterService(
            new Mock<ILogger<SystemRegisterService>>().Object,
            new RegisterManager(_repository.Object, events.Object),
            new TransactionManager(_repository.Object, events.Object),
            new Mock<IValidatorServiceClient>().Object,
            new Mock<ISystemWalletSigningService>().Object,
            hash.Object);
    }

    // ------------------------------------------------------------------ //
    // The predicate                                                       //
    // ------------------------------------------------------------------ //

    [Fact]
    public void IsBlueprintPublication_TheRealPublication_IsTrue()
    {
        // n1 docket 2: seeded by the bootstrapper. Persists as Control — NOT BlueprintPublish —
        // which is exactly why the transaction type cannot be the discriminator.
        var tx = Publication(GovernanceBlueprintId, Day(2));

        SystemRegisterService.IsBlueprintPublication(tx).Should().BeTrue();
    }

    [Theory]
    // n1 dockets 5-12: "Propose Change".
    [InlineData(1u, "GovernanceOperation")]
    // n1 docket 13 (two of them, in the same docket): "Collect Quorum".
    [InlineData(2u, "GovernanceApproval")]
    // n1 docket 14: "Record Control Transaction" — the enactment that bricked the node.
    [InlineData(4u, "GovernanceOperation")]
    public void IsBlueprintPublication_GovernanceActivity_IsFalse(uint actionId, string trackingType)
    {
        var tx = GovernanceTransaction(actionId, trackingType, Day(14));

        SystemRegisterService.IsBlueprintPublication(tx)
            .Should().BeFalse("a governance transaction names the governance blueprint, it does not publish it");
    }

    [Fact]
    public void IsBlueprintPublication_PostIssue876BlueprintPublishType_IsTrue()
    {
        var tx = Publication("register-creation-v1", Day(1));
        tx.MetaData!.TransactionType = Sorcha.Register.Models.Enums.TransactionType.BlueprintPublish;
        tx.MetaData.TrackingData = null;

        SystemRegisterService.IsBlueprintPublication(tx).Should().BeTrue();
    }

    [Fact]
    public void IsBlueprintPublication_PreMarkerControlPublication_IsTrue()
    {
        // A publication old enough to predate the TrackingData marker. It is still not an action
        // submission, so it carries no action id — which is what the fallback keys on.
        var tx = Publication("legacy-v1", Day(1));
        tx.MetaData!.TrackingData = null;

        SystemRegisterService.IsBlueprintPublication(tx).Should().BeTrue();
    }

    [Fact]
    public void IsBlueprintPublication_PreMarkerControlCarryingAnActionId_IsFalse()
    {
        var tx = GovernanceTransaction(1u, "GovernanceOperation", Day(9));
        tx.MetaData!.TrackingData = null;

        SystemRegisterService.IsBlueprintPublication(tx).Should().BeFalse();
    }

    [Fact]
    public void IsBlueprintPublication_Genesis_IsFalse()
    {
        var tx = Publication("genesis", Day(0));

        SystemRegisterService.IsBlueprintPublication(tx).Should().BeFalse();
    }

    // ------------------------------------------------------------------ //
    // What the lookup returns                                             //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetBlueprintAsync_AfterGovernanceActivity_StillReturnsThePublishedBlueprint()
    {
        // The n1 shape: one real publication, then governance transactions all naming it, all
        // newer. Before the fix the newest match won and the caller got an enactment record.
        GivenLedger(
            Publication(GovernanceBlueprintId, Day(2)),
            GovernanceTransaction(1u, "GovernanceOperation", Day(5)),
            GovernanceTransaction(1u, "GovernanceOperation", Day(12)),
            GovernanceTransaction(2u, "GovernanceApproval", Day(13)),
            GovernanceTransaction(4u, "GovernanceOperation", Day(14)));

        var entry = await _service.GetBlueprintAsync(GovernanceBlueprintId);

        entry.Should().NotBeNull();
        entry!.PublishedAt.Should().Be(Day(2), "the publication is the answer, not the newest transaction naming it");

        // The payload must be the blueprint. This assertion maps directly onto the live failure:
        // a governance payload deserializes fine and simply has no actions.
        entry.Document.Should().NotBeNull();
        entry.Document!.RootElement.TryGetProperty("actions", out var actions).Should().BeTrue();
        actions.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetAllBlueprintsAsync_AfterGovernanceActivity_ListsEachBlueprintOnce()
    {
        GivenLedger(
            Publication("register-creation-v1", Day(1)),
            Publication(GovernanceBlueprintId, Day(2)),
            GovernanceTransaction(1u, "GovernanceOperation", Day(5)),
            GovernanceTransaction(4u, "GovernanceOperation", Day(14)));

        var all = await _service.GetAllBlueprintsAsync();

        all.Select(b => b.BlueprintId)
           .Should().BeEquivalentTo(new[] { "register-creation-v1", GovernanceBlueprintId });
    }

    // ------------------------------------------------------------------ //
    // What the version number means                                       //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetBlueprintAsync_PublishedOnce_IsVersionOne_HoweverMuchGovernanceFollows()
    {
        // The reported symptom: register-governance-v1 climbing v2 -> v5 on a node where it had
        // been published exactly once. The number was a position in the transaction list.
        GivenLedger(
            Publication("register-creation-v1", Day(1)),
            Publication(GovernanceBlueprintId, Day(2)),
            Publication("create-organisation-v1", Day(3)),
            GovernanceTransaction(1u, "GovernanceOperation", Day(5)),
            GovernanceTransaction(2u, "GovernanceApproval", Day(13)),
            GovernanceTransaction(4u, "GovernanceOperation", Day(14)));

        var entry = await _service.GetBlueprintAsync(GovernanceBlueprintId);

        entry!.Version.Should().Be(1, "it has been published once");
    }

    [Fact]
    public async Task GetBlueprintAsync_RepublishedTwice_IsVersionTwo()
    {
        GivenLedger(
            Publication("register-creation-v1", Day(1)),
            Publication(GovernanceBlueprintId, Day(2)),
            Publication(GovernanceBlueprintId, Day(6)));

        var entry = await _service.GetBlueprintAsync(GovernanceBlueprintId);

        entry!.Version.Should().Be(2);
        entry.PublishedAt.Should().Be(Day(6), "the newest publication is the current one");
    }

    [Fact]
    public async Task GetAllBlueprintsAsync_VersionsAreCountedPerBlueprint()
    {
        GivenLedger(
            Publication("bp-a", Day(1)),
            Publication("bp-b", Day(2)),
            Publication("bp-a", Day(3)));

        var all = await _service.GetAllBlueprintsAsync();

        all.Single(b => b.BlueprintId == "bp-b").Version
           .Should().Be(1, "publishing bp-a must not advance bp-b's version");
        all.Where(b => b.BlueprintId == "bp-a").Select(b => b.Version)
           .Should().BeEquivalentTo(new long[] { 1, 2 });
    }

    // ------------------------------------------------------------------ //
    // Fixtures                                                            //
    // ------------------------------------------------------------------ //

    private static DateTime Day(int n) => new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc).AddMinutes(n);

    private void GivenLedger(params TransactionModel[] transactions) =>
        _repository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

    /// <summary>A genuine blueprint publication, shaped as the bootstrapper writes it.</summary>
    private static TransactionModel Publication(string blueprintId, DateTime timestamp)
    {
        var body = "{\"id\":\"" + blueprintId + "\",\"title\":\"Register Governance\","
                 + "\"actions\":[{\"id\":1,\"title\":\"Propose Change\"}]}";

        return Transaction(blueprintId, timestamp, body, actionId: null, new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = "BlueprintPublish",
            ["BlueprintId"] = blueprintId,
            ["publishedBy"] = "system"
        });
    }

    /// <summary>
    /// A governance transaction, shaped as the ledger actually holds it: it names the governance
    /// blueprint, carries the action id it was submitted against, and its payload is an operation
    /// record — valid JSON that is not a blueprint.
    /// </summary>
    private static TransactionModel GovernanceTransaction(uint actionId, string trackingType, DateTime timestamp)
    {
        const string body = "{\"registerId\":\"aebf26362e079087571ac0932d4db973\","
                          + "\"operationType\":\"transfer\",\"proposalStatus\":\"Recorded\"}";

        return Transaction(GovernanceBlueprintId, timestamp, body, actionId, new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = trackingType,
            ["operationType"] = "transfer"
        });
    }

    private static TransactionModel Transaction(
        string blueprintId,
        DateTime timestamp,
        string payloadJson,
        uint? actionId,
        Dictionary<string, string> trackingData) => new TransactionModel
        {
            TxId = $"tx-{blueprintId}-{actionId?.ToString() ?? "pub"}-{timestamp.Ticks}".PadRight(64, '0')[..64],
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            SenderWallet = "system",
            TimeStamp = timestamp,
            MetaData = new TransactionMetaData
            {
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                // Control, not BlueprintPublish — both publications and governance persist this way.
                TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control,
                BlueprintId = blueprintId,
                ActionId = actionId,
                TrackingData = trackingData
            },
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson)),
                    Hash = "fakehash",
                    ContentType = "application/json",
                    ContentEncoding = "base64url"
                }
            },
            Signature = "system-signature"
        };
}
