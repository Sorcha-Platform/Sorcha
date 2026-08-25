// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
/// T035 — unit tests for <c>BuildPresentationOutcomeAsync</c> (Feature 111).
/// Validates success/decline payload shape, OutcomeDetailLevel gating of
/// verifierDiagnostics, and RecipientsWallets population.
/// </summary>
public class TransactionBuilderServicePresentationOutcomeTests
{
    private readonly TransactionBuilderService _service = new(
        new Mock<ICryptoModule>().Object,
        new Mock<IHashProvider>().Object,
        new Mock<ISymmetricCrypto>().Object,
        new Mock<ILogger<TransactionBuilderService>>().Object);

    private static BlueprintModel MakeBp() => new()
    {
        Id = "bp-1", Title = "t", Description = "d", Version = 1,
        Participants = [], Actions = []
    };
    private static Instance MakeInst() => new()
    {
        Id = Guid.NewGuid().ToString(), BlueprintId = "bp-1", BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", BlueprintVersion = 1,
        RegisterId = "reg-1", TenantId = "t"
    };
    private static ActionModel MakeAct() => new() { Id = 3, BlueprintId = "bp-1" };

    [Fact]
    public async Task Success_PayloadCarriesVerifiedClaimsAndHash()
    {
        var built = await _service.BuildPresentationOutcomeAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            submitterWallet: "ws11qcitizen",
            outcomeKind: "success",
            verifiedClaims: new Dictionary<string, object> { ["name"] = "Alice" },
            declineReason: null,
            verifierDiagnostics: null,
            presentationSubmissionHash: "sha256:abc",
            actionPayload: new Dictionary<string, object> { ["project"] = "X" },
            previousTransactionId: null);

        using var doc = JsonDocument.Parse(built.TransactionData);
        var root = doc.RootElement;
        root.GetProperty("kind").GetString().Should().Be("success");
        root.GetProperty("verifiedClaims").GetProperty("name").GetString().Should().Be("Alice");
        root.GetProperty("presentationSubmissionHash").GetString().Should().Be("sha256:abc");
        root.GetProperty("actionPayload").GetProperty("project").GetString().Should().Be("X");
        root.TryGetProperty("reason", out _).Should().BeFalse();
        built.TransactionType.Should().Be("presentation-outcome");
    }

    [Fact]
    public async Task Decline_Minimal_OmitsDiagnostics()
    {
        var built = await _service.BuildPresentationOutcomeAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            submitterWallet: "ws11qcitizen",
            outcomeKind: "decline",
            verifiedClaims: null,
            declineReason: "ExpiredCredential",
            verifierDiagnostics: null,
            presentationSubmissionHash: null,
            actionPayload: null,
            previousTransactionId: null);

        using var doc = JsonDocument.Parse(built.TransactionData);
        var root = doc.RootElement;
        root.GetProperty("kind").GetString().Should().Be("decline");
        root.GetProperty("reason").GetString().Should().Be("ExpiredCredential");
        root.TryGetProperty("verifierDiagnostics", out _).Should().BeFalse();
        root.TryGetProperty("verifiedClaims", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Decline_Verbose_IncludesDiagnostics()
    {
        var built = await _service.BuildPresentationOutcomeAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            submitterWallet: "ws11qcitizen",
            outcomeKind: "decline",
            verifiedClaims: null,
            declineReason: "SchemaMismatch",
            verifierDiagnostics: new Dictionary<string, object> { ["schema"] = "v2" },
            presentationSubmissionHash: null,
            actionPayload: null,
            previousTransactionId: null);

        using var doc = JsonDocument.Parse(built.TransactionData);
        doc.RootElement.GetProperty("verifierDiagnostics").GetProperty("schema").GetString().Should().Be("v2");
    }

    [Fact]
    public async Task Outcome_RecipientsWalletsHasSubmitter()
    {
        var built = await _service.BuildPresentationOutcomeAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            submitterWallet: "ws11qcitizen",
            outcomeKind: "success",
            verifiedClaims: new Dictionary<string, object>(),
            declineReason: null,
            verifierDiagnostics: null,
            presentationSubmissionHash: "h",
            actionPayload: null,
            previousTransactionId: null);

        built.RecipientsWallets.Should().ContainSingle().Which.Should().Be("ws11qcitizen");
    }
}
