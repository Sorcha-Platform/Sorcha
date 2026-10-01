// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Storage.InMemory;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>
/// Feature 197: the current system blueprint is decided by LEDGER order (docket, then position in
/// the docket), never by <see cref="TransactionModel.TimeStamp"/>, which the submitting node sets.
/// </summary>
public class SystemRegisterServiceCurrencyTests
{
    private const string BlueprintId = "register-governance-v1";
    private static readonly DateTime T0 = new(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryRegisterRepository _repository = new();
    private readonly SystemRegisterService _service;

    public SystemRegisterServiceCurrencyTests()
    {
        var events = new Mock<IEventPublisher>();
        var hash = new Mock<IHashProvider>();

        _repository.InsertRegisterAsync(new Sorcha.Register.Models.Register
        {
            Id = SystemRegisterConstants.SystemRegisterId,
            Name = SystemRegisterConstants.SystemRegisterName,
            Height = 0,
            Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
        }).GetAwaiter().GetResult();

        _service = new SystemRegisterService(
            NullLogger<SystemRegisterService>.Instance,
            new RegisterManager(_repository, events.Object),
            new TransactionManager(_repository, events.Object),
            new Mock<IValidatorServiceClient>().Object,
            new Mock<ISystemWalletSigningService>().Object,
            hash.Object);
    }

    [Fact]
    public async Task GetBlueprintAsync_LaterDocketWithEarlierTimeStamp_IsCurrentAndVersionTwo()
    {
        // Sealed first, but stamped LATER by its submitting node.
        var sealedFirst = await GivenSealedPublicationAsync("tx-first", docket: 3, T0.AddHours(5), title: "first");
        // Sealed second, stamped EARLIER. Timestamp ordering would pick `sealedFirst`.
        var sealedSecond = await GivenSealedPublicationAsync("tx-second", docket: 4, T0, title: "second");

        var entry = await _service.GetBlueprintAsync(BlueprintId);

        entry.Should().NotBeNull();
        entry!.PublicationTransactionId.Should().Be(sealedSecond.TxId);
        entry.PublicationTransactionId.Should().NotBe(sealedFirst.TxId);
        entry.Version.Should().Be(2);
    }

    [Fact]
    public async Task GetAllBlueprintsAsync_VersionsFollowLedgerOrderNotTimeStamp()
    {
        await GivenSealedPublicationAsync("tx-first", docket: 3, T0.AddHours(5), title: "first");
        await GivenSealedPublicationAsync("tx-second", docket: 4, T0, title: "second");

        var all = await _service.GetAllBlueprintsAsync();

        all.Single(e => e.PublicationTransactionId == "tx-first").Version.Should().Be(1);
        all.Single(e => e.PublicationTransactionId == "tx-second").Version.Should().Be(2);
    }

    [Fact]
    public async Task GetPublicationsAsync_ReturnsLedgerOrder_AndExcludesUnsealed()
    {
        await GivenSealedPublicationAsync("tx-a", docket: 7, T0.AddHours(9), title: "a");
        await GivenSealedPublicationAsync("tx-b", docket: 8, T0, title: "b");
        await GivenPublicationAsync("tx-pending", docketNumber: null, T0.AddHours(20), title: "pending");

        var publications = await _service.GetPublicationsAsync(BlueprintId);

        publications.Select(t => t.TxId).Should().Equal("tx-a", "tx-b");
    }

    [Fact]
    public async Task GetBlueprintAsync_UnsealedNewestPublication_DoesNotDisplaceTheSealedOne()
    {
        await GivenSealedPublicationAsync("tx-sealed", docket: 2, T0, title: "sealed");
        await GivenPublicationAsync("tx-pending", docketNumber: null, T0.AddDays(1), title: "pending");

        var entry = await _service.GetBlueprintAsync(BlueprintId);

        entry!.PublicationTransactionId.Should().Be("tx-sealed");
        entry.Version.Should().Be(1);
    }

    private async Task<TransactionModel> GivenSealedPublicationAsync(
        string txId, ulong docket, DateTime timestamp, string title)
    {
        var tx = await GivenPublicationAsync(txId, docket, timestamp, title);
        await _repository.InsertDocketAsync(new DocketHeader
        {
            Id = docket,
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            TransactionIds = new List<string> { txId }
        });
        return tx;
    }

    private async Task<TransactionModel> GivenPublicationAsync(
        string txId, ulong? docketNumber, DateTime timestamp, string title)
    {
        var body = "{\"id\":\"" + BlueprintId + "\",\"title\":\"" + title + "\","
                 + "\"actions\":[{\"id\":1,\"title\":\"Propose Change\"}]}";

        var tx = new TransactionModel
        {
            TxId = txId,
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            SenderWallet = "system",
            TimeStamp = timestamp,
            DocketNumber = docketNumber,
            MetaData = new TransactionMetaData
            {
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control,
                BlueprintId = BlueprintId,
                TrackingData = new Dictionary<string, string>
                {
                    ["Type"] = "Control",
                    ["transactionType"] = "BlueprintPublish",
                    ["BlueprintId"] = BlueprintId,
                    ["publishedBy"] = "system"
                }
            },
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(body)),
                    Hash = "fakehash",
                    ContentType = "application/json",
                    ContentEncoding = "base64url"
                }
            },
            Signature = "system-signature"
        };

        await _repository.InsertTransactionAsync(tx);
        return tx;
    }
}
