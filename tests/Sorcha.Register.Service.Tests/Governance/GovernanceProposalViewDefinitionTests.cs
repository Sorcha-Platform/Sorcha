// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Storage.InMemory;
using Sorcha.Serialization;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Governance;

/// <summary>
/// Feature 197 T017 — the proposal audit view reports the governance definition the proposal was
/// raised under: its OWN sealed pin, resolved to a ledger-order version, never the latest publication.
/// An unpinned (legacy) proposal reports no pin and says so.
/// </summary>
public sealed class GovernanceProposalViewDefinitionTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";
    private const string ProposalTx = "proposal-tx";
    private const string PinV1 = "publication-v1";
    private const string PinV2 = "publication-v2";
    private const string PinV3 = "publication-v3";

    private readonly Mock<IReadOnlyRegisterRepository> _repository = new();
    private readonly Mock<IGovernanceRosterService> _roster = new();
    private readonly Mock<SystemRegisterService> _systemRegister = new(
        NullLogger<SystemRegisterService>.Instance,
        new RegisterManager(new InMemoryRegisterRepository(), new Mock<IEventPublisher>().Object),
        new TransactionManager(new InMemoryRegisterRepository(), new Mock<IEventPublisher>().Object),
        new Mock<IValidatorServiceClient>().Object,
        new Mock<ISystemWalletSigningService>().Object,
        new Mock<IHashProvider>().Object);

    private static TransactionModel Publication(string txId) => new() { TxId = txId, RegisterId = "system" };

    private static GovernanceOperation Operation() => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = "did:sorcha:w:ws11qadmin",
        TargetDid = "did:sorcha:w:ws11qnew",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = DateTimeOffset.UnixEpoch,
    };

    private GovernanceProposalViewService Service(string? pin)
    {
        var tx = new TransactionModel
        {
            TxId = ProposalTx,
            RegisterId = RegisterId,
            MetaData = new TransactionMetaData
            {
                TrackingData = new Dictionary<string, string> { ["transactionType"] = "GovernanceOperation" }
            },
            Payloads =
            [
                new PayloadModel
                {
                    Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                        new ControlTransactionPayload
                        {
                            Version = 1,
                            Operation = Operation(),
                            GovernanceDefinitionTxId = pin,
                        }))
                }
            ],
        };

        _repository.Setup(r => r.GetTransactionAsync(RegisterId, ProposalTx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);
        _repository.Setup(r => r.GetTransactionsByTypeAsync(
                RegisterId, TransactionType.Control, It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        // Three publications, oldest first; the LATEST (v3) is deliberately not what any test pins to
        // except where stated, so reading "latest" instead of the pin cannot pass.
        _systemRegister.Setup(s => s.GetPublicationsAsync(
                SystemBlueprintCatalog.GovernanceBlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Publication(PinV1), Publication(PinV2), Publication(PinV3)]);

        var reader = new GovernanceProposalReader(
            _repository.Object, NullLogger<GovernanceProposalReader>.Instance);

        return new GovernanceProposalViewService(
            reader, _roster.Object, _repository.Object, systemRegister: _systemRegister.Object);
    }

    [Fact]
    public async Task GetAsync_PinnedToAnOlderPublication_ReportsThatPinAndItsVersionNotTheLatest()
    {
        var view = await Service(PinV2).GetAsync(RegisterId, ProposalTx);

        view.Should().NotBeNull();
        view!.GoverningDefinitionTxId.Should().Be(PinV2);
        view.GoverningDefinitionVersion.Should().Be(2, "v3 is current, but the proposal was raised under v2");
        view.GoverningDefinitionLegacy.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_PinnedToTheCurrentPublication_ReportsItsVersion()
    {
        var view = await Service(PinV3).GetAsync(RegisterId, ProposalTx);

        view!.GoverningDefinitionTxId.Should().Be(PinV3);
        view.GoverningDefinitionVersion.Should().Be(3);
    }

    [Fact]
    public async Task GetAsync_PinNotAmongPublications_KeepsThePinAndHasNoVersion()
    {
        var view = await Service("publication-unknown").GetAsync(RegisterId, ProposalTx);

        view!.GoverningDefinitionTxId.Should().Be("publication-unknown");
        view.GoverningDefinitionVersion.Should().BeNull();
        view.GoverningDefinitionLegacy.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_UnpinnedProposal_IsLegacyWithNoPinOrVersion()
    {
        var view = await Service(pin: null).GetAsync(RegisterId, ProposalTx);

        view!.GoverningDefinitionTxId.Should().BeNull();
        view.GoverningDefinitionVersion.Should().BeNull();
        view.GoverningDefinitionLegacy.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_PinnedProposal_CarriesThePin()
    {
        var views = await Service(PinV1).ListAsync(RegisterId, state: null);

        views.Should().ContainSingle().Which.GoverningDefinitionTxId.Should().Be(PinV1);
    }

    [Fact]
    public async Task View_UnderSorchaJson_UsesCamelCaseNames()
    {
        var view = await Service(PinV2).GetAsync(RegisterId, ProposalTx);

        var json = JsonSerializer.Serialize(view, SorchaJson.Options);

        json.Should().Contain("\"governingDefinitionTxId\":\"publication-v2\"")
            .And.Contain("\"governingDefinitionVersion\":2")
            .And.Contain("\"governingDefinitionLegacy\":false");
    }
}
