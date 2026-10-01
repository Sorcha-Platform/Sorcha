// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Storage.InMemory;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.Wallet.Contracts.Constants;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>Feature 197 T019: the operator publish decision order and its refusals.</summary>
public class SystemBlueprintPublishServiceTests
{
    private const string Bp = "register-creation-v1";
    private static readonly byte[] NodeKey = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly JsonElement Definition = JsonDocument.Parse("{\"id\":\"x\"}").RootElement.Clone();

    private readonly Mock<ISystemBlueprintCatalogSource> _catalog = new();
    private readonly Mock<SystemRegisterService> _register;
    private readonly Mock<ISystemWalletSigningService> _signing = new();
    private readonly Mock<IGovernanceRosterService> _roster = new();
    private IReadOnlyList<TransactionModel> _publications = [];
    private string? _candidate = "img";

    public SystemBlueprintPublishServiceTests()
    {
        _register = new Mock<SystemRegisterService>(
            NullLogger<SystemRegisterService>.Instance,
            new RegisterManager(new InMemoryRegisterRepository(), new Mock<IEventPublisher>().Object),
            new TransactionManager(new InMemoryRegisterRepository(), new Mock<IEventPublisher>().Object),
            new Mock<IValidatorServiceClient>().Object,
            new Mock<ISystemWalletSigningService>().Object,
            new Mock<IHashProvider>().Object);
        _register.Setup(r => r.GetPublicationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _publications);
        _register.Setup(r => r.PublishBlueprintAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemRegisterEntry { BlueprintId = Bp, PublicationTransactionId = "new-tx" });

        _catalog.Setup(c => c.TryLoad(It.IsAny<string>())).Returns(Definition);
        _catalog.Setup(c => c.TryComputePublicationId(It.IsAny<string>())).Returns(() => _candidate);

        _signing.Setup(s => s.SignAsync(
                SystemRegisterConstants.SystemRegisterId, "publisher-roster-key-derivation",
                new string('0', 64), SorchaDerivationPaths.BlueprintPublish, "ValidatorKeyDerivation",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult { Signature = [], WalletAddress = "node", PublicKey = NodeKey, Algorithm = "ED25519" });

        SetRoster(Entry(SorchaDerivationPaths.BlueprintPublish, ValidatorKeyStatus.Active, NodeKey));
    }

    private static ValidatorRosterEntry Entry(string context, ValidatorKeyStatus status, byte[] key) => new()
    {
        ValidatorId = "node",
        PublicKey = Convert.ToBase64String(key),
        DerivationContext = context,
        Status = status
    };

    private void SetRoster(params ValidatorRosterEntry[] entries)
        => _roster.Setup(r => r.GetCurrentRosterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminRoster
            {
                ControlRecord = new RegisterControlRecord
                {
                    Validators = new ValidatorRoster { Validators = [.. entries] }
                }
            });

    private static TransactionModel Tx(string id) => new() { TxId = id };

    private SystemBlueprintPublishService Build() => new(
        _catalog.Object, _register.Object, _signing.Object, _roster.Object, TimeProvider.System,
        NullLogger<SystemBlueprintPublishService>.Instance);

    private void VerifyNothingSubmitted() => _register.Verify(r => r.PublishBlueprintAsync(
        It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string>(),
        It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task PublishAsync_NotACatalogueBlueprint_NotFound()
    {
        var d = await Build().PublishAsync("not-a-system-blueprint", false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.NotFound);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_NoTemplateShipped_NotFound()
    {
        _catalog.Setup(c => c.TryLoad(Bp)).Returns((JsonElement?)null);

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.NotFound);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_NodeKeyNotOnRoster_NoPublishingKey()
    {
        SetRoster(Entry(SorchaDerivationPaths.BlueprintPublish, ValidatorKeyStatus.Active, [9, 9, 9]));

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.NoPublishingKey);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_KeyOnRosterUnderWrongContext_NoPublishingKey()
    {
        SetRoster(Entry(SorchaDerivationPaths.DocketSigning, ValidatorKeyStatus.Active, NodeKey));

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.NoPublishingKey);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_KeyRosterEntryNotActive_NoPublishingKey()
    {
        SetRoster(Entry(SorchaDerivationPaths.BlueprintPublish, ValidatorKeyStatus.Rotated, NodeKey));

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.NoPublishingKey);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_RosterReadThrows_NoPublishingKey()
    {
        _roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.NoPublishingKey);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_ImageIsCurrent_Noop()
    {
        _publications = [Tx("a"), Tx("img")];

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.Noop);
        d.DriftState.Should().Be(SystemBlueprintDriftState.InSync);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_ImageBehind_RefusedRollback()
    {
        _publications = [Tx("img"), Tx("newer")];

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.RefusedRollback);
        d.CurrentPublicationTxId.Should().Be("newer");
        d.CandidatePublicationTxId.Should().Be("img");
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_StateUnreadable_StateUnknown()
    {
        _register.Setup(r => r.GetPublicationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ledger down"));

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.StateUnknown);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_ImageHasNoId_StateUnknown()
    {
        _candidate = null;
        _publications = [Tx("a")];

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.StateUnknown);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_ExpectedCurrentDiffers_RefusedConcurrency()
    {
        _publications = [Tx("old")];

        var d = await Build().PublishAsync(Bp, false, "something-else", "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.RefusedConcurrency);
        d.DriftState.Should().Be(SystemBlueprintDriftState.ImageAhead);
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_DryRun_DecidesButSubmitsNothing()
    {
        _publications = [Tx("old")];

        var d = await Build().PublishAsync(Bp, true, "old", "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.DryRun);
        d.CurrentPublicationTxId.Should().Be("old");
        d.CandidatePublicationTxId.Should().Be("img");
        VerifyNothingSubmitted();
    }

    [Fact]
    public async Task PublishAsync_ImageAhead_SubmitsWithOperatorMetadata()
    {
        _publications = [Tx("old")];

        var d = await Build().PublishAsync(Bp, false, "old", "operator-1");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.Submitted);
        d.TransactionId.Should().Be("new-tx");
        _register.Verify(r => r.PublishBlueprintAsync(
            Bp, It.IsAny<JsonElement>(), "operator-1",
            It.Is<Dictionary<string, string>?>(m => m != null && m["seedReason"] == "operator"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Submitted_CarriesVersionsAndPublishingWalletForTheSuccessLog()
    {
        _publications = [Tx("v1-tx")];
        _register.Setup(r => r.PublishBlueprintAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemRegisterEntry
            {
                BlueprintId = Bp, PublicationTransactionId = "new-tx", Version = 2,
                Metadata = new Dictionary<string, string> { ["SystemWalletAddress"] = "node-wallet" },
            });

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.Submitted);
        d.PreviousVersion.Should().Be(1);
        d.NewVersion.Should().Be(2);
        d.PublisherWalletAddress.Should().Be("node-wallet");
    }

    [Fact]
    public async Task PublishAsync_Missing_Submits()
    {
        _publications = [];

        var d = await Build().PublishAsync(Bp, false, null, "op");

        d.Outcome.Should().Be(SystemBlueprintPublishOutcome.Submitted);
        d.DriftState.Should().Be(SystemBlueprintDriftState.Missing);
    }
}
