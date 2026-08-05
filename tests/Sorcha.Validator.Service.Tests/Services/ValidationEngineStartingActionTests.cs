// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Tests for VAL_BP_002 starting action skip in ValidationEngine (US1).
/// Starting actions accept any wallet — validator should not reject.
/// </summary>
public class ValidationEngineStartingActionTests
{
    private readonly Mock<IBlueprintCache> _blueprintCacheMock;
    private readonly Mock<IHashProvider> _hashProviderMock;
    private readonly Mock<ICryptoModule> _cryptoModuleMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IRightsEnforcementService> _rightsEnforcementMock;
    private readonly Mock<IWalletUtilities> _walletUtilitiesMock;
    private readonly Mock<ILogger<ValidationEngine>> _loggerMock;
    private readonly ValidationEngine _engine;

    public ValidationEngineStartingActionTests()
    {
        _blueprintCacheMock = new Mock<IBlueprintCache>();
        _hashProviderMock = new Mock<IHashProvider>();
        _cryptoModuleMock = new Mock<ICryptoModule>();
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _rightsEnforcementMock = new Mock<IRightsEnforcementService>();
        _walletUtilitiesMock = new Mock<IWalletUtilities>();
        _loggerMock = new Mock<ILogger<ValidationEngine>>();

        // Default: no existing successors
        _registerClientMock.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 1 });

        // Default: governance validation passes
        _rightsEnforcementMock.Setup(r => r.ValidateGovernanceRightsAsync(
                It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) =>
                ValidationEngineResult.Success(tx.TransactionId, tx.RegisterId, TimeSpan.Zero));

        var config = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = false,
            EnableSignatureVerification = false,
            EnableChainValidation = false,
            EnableBlueprintConformance = true,
            EnableParallelValidation = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };

        _engine = new ValidationEngine(
            Options.Create(config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateBlueprintConformance_StartingAction_AcceptsAnyWallet()
    {
        // Arrange: starting action with any wallet — should be accepted (no VAL_BP_002)
        var blueprint = CreateBlueprint();
        var tx = CreateTransaction(actionId: "0", isStarting: true);

        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync("bp-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns("ws11q-random-wallet");

        SetupHashValidation(tx);

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert: should succeed with no VAL_BP_002 error
        result.Errors.Should().NotContain(e => e.Code == "VAL_BP_002");
    }

    [Fact]
    public async Task ValidateBlueprintConformance_NonStartingAction_WrongWallet_RejectsWithBP002()
    {
        // Arrange: non-starting action, derived wallet doesn't match participant's hardcoded wallet
        var blueprint = CreateBlueprint();
        var tx = CreateTransaction(actionId: "1", isStarting: false);

        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync("bp-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns("ws11q-wrong-wallet"); // Doesn't match "ws11q-reviewer"

        SetupHashValidation(tx);

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert: VAL_BP_002 error should be present
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_002");
    }

    #region Helpers

    private void SetupHashValidation(Transaction tx)
    {
        var hashBytes = Convert.FromHexString(tx.PayloadHash);
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(hashBytes);
    }

    private static BlueprintModel CreateBlueprint() => new()
    {
        Id = "bp-test",
        Title = "Test Blueprint",
        Participants =
        [
            new ParticipantModel { Id = "citizen", Name = "Citizen" }, // No wallet — dynamic binding
            new ParticipantModel { Id = "reviewer", Name = "Reviewer", WalletAddress = "ws11q-reviewer" }
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 0,
                Title = "Apply",
                Sender = "citizen",
                IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [1] }]
            },
            new ActionModel
            {
                Id = 1,
                Title = "Review",
                Sender = "reviewer"
            }
        ]
    };

    private static Transaction CreateTransaction(string actionId, bool isStarting)
    {
        var payloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        return new Transaction
        {
            TransactionId = $"tx-{Guid.NewGuid():N}",
            RegisterId = "reg-test",
            BlueprintId = "bp-test",
            ActionId = actionId,
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = payloadHash,
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = isStarting ? null : "tx-prev-0000",
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
    }

    #endregion
}
