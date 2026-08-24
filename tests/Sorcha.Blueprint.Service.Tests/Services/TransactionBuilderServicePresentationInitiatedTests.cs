// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Interfaces;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using Instance = Sorcha.Blueprint.Service.Models.Instance;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// T022 — unit tests for BuildPresentationInitiatedAsync (Feature 111).
/// Verifies the builder emits the right transaction shape with no credential
/// data, populates metadata, and sets RecipientsWallets to the submitter.
/// </summary>
public class TransactionBuilderServicePresentationInitiatedTests
{
    private readonly TransactionBuilderService _service;

    public TransactionBuilderServicePresentationInitiatedTests()
    {
        _service = new TransactionBuilderService(
            new Mock<ICryptoModule>().Object,
            new Mock<IHashProvider>().Object,
            new Mock<ISymmetricCrypto>().Object,
            new Mock<ILogger<TransactionBuilderService>>().Object);
    }

    private static BlueprintModel MakeBlueprint() => new()
    {
        Id = "bp-111-test",
        Title = "Test blueprint",
        Description = "Unit test blueprint",
        Version = 1,
        Participants = [],
        Actions = []
    };

    private static Instance MakeInstance() => new()
    {
        Id = Guid.NewGuid().ToString(),
        BlueprintId = "bp-111-test",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-unit-test",
        TenantId = "tenant-unit-test"
    };

    private static ActionModel MakeAction() => new()
    {
        Id = 3,
        Title = "Submit evidence",
        BlueprintId = "bp-111-test"
    };

    [Fact]
    public async Task BuildPresentationInitiatedAsync_PopulatesCoreFields()
    {
        var bp = MakeBlueprint();
        var instance = MakeInstance();
        var action = MakeAction();
        var requestId = Guid.NewGuid();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("{\"requirements\":1}"));

        var built = await _service.BuildPresentationInitiatedAsync(
            bp, instance, action,
            presentationRequestId: requestId,
            consumerName: "haip",
            requirementsDigest: digest,
            validityWindowSeconds: 600,
            submitterWallet: "ws11qwallet",
            previousTransactionId: "prev-tx-hash");

        built.Should().NotBeNull();
        built.TransactionType.Should().Be("presentation-initiated");
        built.RegisterId.Should().Be(instance.RegisterId);
        built.TxId.Should().NotBeNullOrWhiteSpace();
        built.PayloadHash.Should().Be(built.TxId);
    }

    [Fact]
    public async Task BuildPresentationInitiatedAsync_RecipientsWallets_ContainsSubmitter()
    {
        var built = await _service.BuildPresentationInitiatedAsync(
            MakeBlueprint(), MakeInstance(), MakeAction(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            requirementsDigest: SHA256.HashData([0x1]),
            validityWindowSeconds: 600,
            submitterWallet: "ws11qcitizen",
            previousTransactionId: null);

        built.RecipientsWallets.Should().ContainSingle().Which.Should().Be("ws11qcitizen");
    }

    [Fact]
    public async Task BuildPresentationInitiatedAsync_Metadata_CarriesConsumerAndRequestId()
    {
        var requestId = Guid.NewGuid();
        var built = await _service.BuildPresentationInitiatedAsync(
            MakeBlueprint(), MakeInstance(), MakeAction(),
            presentationRequestId: requestId,
            consumerName: "haip",
            requirementsDigest: SHA256.HashData([0x2]),
            validityWindowSeconds: 600,
            submitterWallet: "ws11qcitizen",
            previousTransactionId: null);

        built.Metadata.Should().ContainKey("consumerName").WhoseValue.Should().Be("haip");
        built.Metadata.Should().ContainKey("presentationRequestId")
            .WhoseValue.Should().Be(requestId.ToString());
        built.Metadata.Should().ContainKey("actionId").WhoseValue.Should().Be(3);
    }

    [Fact]
    public async Task BuildPresentationInitiatedAsync_PayloadHasNoCredentialFields()
    {
        var built = await _service.BuildPresentationInitiatedAsync(
            MakeBlueprint(), MakeInstance(), MakeAction(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            requirementsDigest: SHA256.HashData([0x3]),
            validityWindowSeconds: 600,
            submitterWallet: "ws11qcitizen",
            previousTransactionId: null);

        using var doc = JsonDocument.Parse(built.TransactionData);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("presentation-initiated");

        // FR-002 invariant: MUST NOT contain credential content.
        root.TryGetProperty("verifiedClaims", out _).Should().BeFalse();
        root.TryGetProperty("claims", out _).Should().BeFalse();
        root.TryGetProperty("payload", out _).Should().BeFalse();
        root.TryGetProperty("credentials", out _).Should().BeFalse();
        root.TryGetProperty("presentationSubmissionHash", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BuildPresentationInitiatedAsync_RequirementsDigest_EncodedAsLowercaseHex()
    {
        var digest = new byte[] { 0xAB, 0xCD, 0xEF };
        var built = await _service.BuildPresentationInitiatedAsync(
            MakeBlueprint(), MakeInstance(), MakeAction(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            requirementsDigest: digest,
            validityWindowSeconds: 600,
            submitterWallet: "ws11qcitizen",
            previousTransactionId: null);

        using var doc = JsonDocument.Parse(built.TransactionData);
        doc.RootElement.GetProperty("requirementsDigest").GetString().Should().Be("abcdef");
    }

    [Fact]
    public async Task BuildPresentationInitiatedAsync_DifferentRequestIds_ProduceDifferentTxIds()
    {
        var a = await _service.BuildPresentationInitiatedAsync(
            MakeBlueprint(), MakeInstance(), MakeAction(),
            Guid.NewGuid(), "haip", SHA256.HashData([0x1]), 600, "ws11q", null);
        var b = await _service.BuildPresentationInitiatedAsync(
            MakeBlueprint(), MakeInstance(), MakeAction(),
            Guid.NewGuid(), "haip", SHA256.HashData([0x1]), 600, "ws11q", null);

        a.TxId.Should().NotBe(b.TxId);
    }
}
