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
/// T060 — unit tests for <c>BuildPresentationAbandonedAsync</c> (Feature 111 US4).
/// Verifies payload shape and no-credential-data invariant (same as initiated).
/// </summary>
public class TransactionBuilderServicePresentationAbandonedTests
{
    private readonly TransactionBuilderService _service = new(
        new Mock<ICryptoModule>().Object,
        new Mock<IHashProvider>().Object,
        new Mock<ISymmetricCrypto>().Object,
        new Mock<ILogger<TransactionBuilderService>>().Object);

    [Fact]
    public async Task Abandoned_PayloadShape()
    {
        var id = Guid.NewGuid();
        var built = await _service.BuildPresentationAbandonedAsync(
            new BlueprintModel { Id = "bp", Title = "t", Description = "d", Version = 1, Participants = [], Actions = [] },
            new Instance { Id = Guid.NewGuid().ToString(), BlueprintId = "bp", BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", BlueprintVersion = 1, RegisterId = "reg-1", TenantId = "t" },
            new ActionModel { Id = 3, BlueprintId = "bp" },
            presentationRequestId: id,
            consumerName: "haip",
            submitterWallet: "ws11qcitizen",
            validityWindowSeconds: 600,
            previousTransactionId: null);

        built.TransactionType.Should().Be("presentation-abandoned");
        built.RecipientsWallets.Should().ContainSingle().Which.Should().Be("ws11qcitizen");

        using var doc = JsonDocument.Parse(built.TransactionData);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("presentation-abandoned");
        root.GetProperty("presentationRequestId").GetGuid().Should().Be(id);
        root.GetProperty("validityWindowSeconds").GetInt32().Should().Be(600);
        root.GetProperty("consumerName").GetString().Should().Be("haip");
        root.TryGetProperty("abandonedAt", out _).Should().BeTrue();

        // FR invariant: no credential data.
        root.TryGetProperty("verifiedClaims", out _).Should().BeFalse();
        root.TryGetProperty("claims", out _).Should().BeFalse();
        root.TryGetProperty("reason", out _).Should().BeFalse();
    }
}
