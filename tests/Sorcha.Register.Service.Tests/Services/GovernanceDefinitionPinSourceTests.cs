// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Storage.InMemory;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>
/// Feature 197 seam: the pin a new proposal is stamped with comes from
/// <see cref="GovernanceDefinitionPinSource"/> over a REAL <see cref="SystemRegisterService"/>. It must be
/// the current entry's publication id — the value the Validator compares raises against — not the
/// payload checksum sitting beside it on the same entry, and a failed read must yield null (fail closed),
/// never an exception into the proposal path.
/// </summary>
public class GovernanceDefinitionPinSourceTests
{
    private const string PayloadHash = "payload-hash-not-an-id";
    private static readonly DateTime T0 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SystemRegisterService Service(IRegisterRepository repository)
    {
        var events = new Mock<IEventPublisher>();
        return new SystemRegisterService(
            NullLogger<SystemRegisterService>.Instance,
            new RegisterManager(repository, events.Object),
            new TransactionManager(repository, events.Object),
            new Mock<IValidatorServiceClient>().Object,
            new Mock<ISystemWalletSigningService>().Object,
            new Mock<IHashProvider>().Object);
    }

    private static async Task<InMemoryRegisterRepository> RepositoryWithPublicationsAsync(params string[] txIds)
    {
        var repository = new InMemoryRegisterRepository();
        await repository.InsertRegisterAsync(new Sorcha.Register.Models.Register
        {
            Id = SystemRegisterConstants.SystemRegisterId,
            Name = SystemRegisterConstants.SystemRegisterName,
            Height = 0,
            Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
        });

        ulong docket = 1;
        foreach (var txId in txIds)
        {
            var body = "{\"id\":\"" + GovernanceBlueprint.BlueprintId + "\",\"title\":\"" + txId + "\",\"actions\":[]}";
            await repository.InsertTransactionAsync(new TransactionModel
            {
                TxId = txId,
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                SenderWallet = "system",
                TimeStamp = T0.AddHours(docket),
                DocketNumber = docket,
                MetaData = new TransactionMetaData
                {
                    RegisterId = SystemRegisterConstants.SystemRegisterId,
                    TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control,
                    BlueprintId = GovernanceBlueprint.BlueprintId,
                    TrackingData = new Dictionary<string, string>
                    {
                        ["Type"] = "Control",
                        ["transactionType"] = "BlueprintPublish",
                        ["BlueprintId"] = GovernanceBlueprint.BlueprintId,
                        ["publishedBy"] = "system"
                    }
                },
                PayloadCount = 1,
                Payloads =
                [
                    new PayloadModel
                    {
                        Data = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(body)),
                        Hash = PayloadHash,
                        ContentType = "application/json",
                        ContentEncoding = "base64url"
                    }
                ],
                Signature = "system-signature"
            });
            await repository.InsertDocketAsync(new DocketHeader
            {
                Id = docket,
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                TransactionIds = [txId]
            });
            docket++;
        }

        return repository;
    }

    [Fact]
    public async Task GetCurrentAsync_TwoSealedPublications_ReturnsTheCurrentPublicationIdNotTheChecksum()
    {
        var repository = await RepositoryWithPublicationsAsync("governance-v1-tx", "governance-v2-tx");
        var source = new GovernanceDefinitionPinSource(
            Service(repository), NullLogger<GovernanceDefinitionPinSource>.Instance);

        var pin = await source.GetCurrentAsync();

        pin.Should().Be("governance-v2-tx");
        pin.Should().NotBe(PayloadHash);
    }

    [Fact]
    public async Task GetCurrentAsync_NoPublication_ReturnsNull()
    {
        var repository = await RepositoryWithPublicationsAsync();
        var source = new GovernanceDefinitionPinSource(
            Service(repository), NullLogger<GovernanceDefinitionPinSource>.Instance);

        (await source.GetCurrentAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAsync_LedgerReadThrows_ReturnsNullAndLogsWarning()
    {
        var repository = new Mock<IRegisterRepository>();
        repository.Setup(r => r.GetRegisterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ledger unavailable"));
        var logger = new Mock<ILogger<GovernanceDefinitionPinSource>>();
        var source = new GovernanceDefinitionPinSource(Service(repository.Object), logger.Object);

        var pin = await source.GetCurrentAsync();

        pin.Should().BeNull();
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.Is<Exception?>(e => e is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
