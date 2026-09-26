// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// #1704 — no receipts were ever written, so a verification bundle could not be exported for any
/// transaction on an ordinary register. Of the validator's three docket-write paths only the gRPC
/// DocketDistributor generated receipts; the LIVE seal path (DocketBuildTriggerService) did not.
/// </summary>
public class ReceiptPublicationTests
{
    private const string RegisterId = "reg-1704";

    private static Docket SealedDocket() => new()
    {
        DocketId = "docket-6",
        RegisterId = RegisterId,
        DocketNumber = 6,
        DocketHash = "docket-hash-6",
        CreatedAt = DateTimeOffset.UtcNow,
        Transactions = [],
        Status = DocketStatus.Confirmed,
        ProposerValidatorId = "local-validator",
        ProposerSignature = new RegisterSignature
        {
            PublicKey = "pk"u8.ToArray(),
            SignatureValue = "sig"u8.ToArray(),
            Algorithm = "ED25519",
            SignedAt = DateTimeOffset.UtcNow,
        },
        MerkleRoot = "root",
    };

    private static (DocketBuildTriggerService Sut, IServiceScope Scope, Mock<IReceiptPublisher> Publisher, Mock<IRegisterServiceClient> Register)
        LivePath(bool writeSucceeds)
    {
        var register = new Mock<IRegisterServiceClient>();
        register.Setup(r => r.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(writeSucceeds);
        var publisher = new Mock<IReceiptPublisher>();

        var services = new ServiceCollection();
        services.AddSingleton(register.Object);
        services.AddSingleton(Mock.Of<ITransactionPoolPoller>());
        services.AddSingleton(publisher.Object);
        var scope = services.BuildServiceProvider().CreateScope();

        var metrics = new ValidatorMempoolMetrics(
            new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());
        var sut = new DocketBuildTriggerService(
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IRegisterMonitoringRegistry>(),
            Options.Create(new DocketBuildConfiguration()),
            Options.Create(new ValidatorConfiguration { SystemWalletAddress = "test-addr" }),
            Mock.Of<ISystemWalletProvider>(),
            metrics,
            NullLogger<DocketBuildTriggerService>.Instance);

        return (sut, scope, publisher, register);
    }

    [Fact]
    public async Task LiveSealPath_PublishesReceipts_AfterTheDocketIsWritten()
    {
        var (sut, scope, publisher, _) = LivePath(writeSucceeds: true);
        var docket = SealedDocket();

        await sut.WriteDocketAndTransactionsAsync(scope, docket, CancellationToken.None);

        publisher.Verify(p => p.PublishForDocketAsync(docket, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LiveSealPath_DocketWriteRefused_PublishesNoReceipts()
    {
        // A receipt attests to a docket on the register; there is none to attest to.
        var (sut, scope, publisher, _) = LivePath(writeSucceeds: false);

        var act = () => sut.WriteDocketAndTransactionsAsync(scope, SealedDocket(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        publisher.Verify(p => p.PublishForDocketAsync(It.IsAny<Docket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publisher_WritesTheGeneratedReceipts_ForTheDocket()
    {
        var receipt = new TransactionReceipt
        {
            ReceiptId = "r1", TransactionId = "tx1", RegisterId = RegisterId, DocketNumber = 6,
            MerkleRoot = "root", InclusionProof = new MerkleInclusionProof { TransactionHash = "h", MerkleRoot = "root", DocketNumber = 6, ProofPath = [], LeafIndex = 0, TreeSize = 1 },
            Signatures = [], SealedAt = DateTimeOffset.UtcNow,
        };
        var generator = new Mock<IReceiptGenerator>();
        generator.Setup(g => g.GenerateReceiptsForDocketAsync(It.IsAny<Docket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([receipt]);
        var register = new Mock<IRegisterServiceClient>();
        register.Setup(r => r.WriteReceiptBatchAsync(RegisterId, 6, It.IsAny<TransactionReceipt[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var stored = await new ReceiptPublisher(generator.Object, register.Object, NullLogger<ReceiptPublisher>.Instance)
            .PublishForDocketAsync(SealedDocket());

        stored.Should().BeTrue();
        register.Verify(r => r.WriteReceiptBatchAsync(
            RegisterId, 6, It.Is<TransactionReceipt[]>(rs => rs.Length == 1 && rs[0].TransactionId == "tx1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publisher_AFailure_NeverEscapes()
    {
        // The docket is the ledger; a receipt is an attestation about it. Failing to produce one
        // must never undo the docket write that came first.
        var generator = new Mock<IReceiptGenerator>();
        generator.Setup(g => g.GenerateReceiptsForDocketAsync(It.IsAny<Docket>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("wallet down"));

        var act = () => new ReceiptPublisher(generator.Object, Mock.Of<IRegisterServiceClient>(), NullLogger<ReceiptPublisher>.Instance)
            .PublishForDocketAsync(SealedDocket());

        (await act.Should().NotThrowAsync()).Which.Should().BeFalse();
    }
}
