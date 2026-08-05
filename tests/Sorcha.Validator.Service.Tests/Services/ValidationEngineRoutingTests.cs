// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 145 US3 (T021/T022): the validator must treat a carried <see cref="RoutingDecision"/> as a
/// trusted, governed ledger fact. <c>VAL_ROUTING_001</c> rejects a decision whose next actions are not
/// structural successors of the completed action; <c>VAL_ROUTING_002</c> rejects a forged/invalid
/// attestation or one that fails to satisfy the register's <c>routingAttestation</c> governance policy.
/// Parallel branches must be preserved.
/// </summary>
public class ValidationEngineRoutingTests
{
    private readonly Mock<IBlueprintCache> _blueprintCacheMock = new();
    private readonly Mock<IHashProvider> _hashProviderMock = new();
    private readonly Mock<ICryptoModule> _cryptoModuleMock = new();
    private readonly Mock<IWalletUtilities> _walletUtilitiesMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();
    private readonly Mock<IRightsEnforcementService> _rightsEnforcementMock = new();
    private readonly Mock<IGovernanceRosterService> _rosterMock = new();
    private readonly Mock<ILogger<ValidationEngine>> _loggerMock = new();

    private const string RegisterId = "test-register";
    private const string BlueprintId = "bp-routing";

    private ValidationEngine CreateEngine(AttestationKind? requiredStrength = null)
    {
        // Hash provider returns a fixed digest; the crypto module decides validity.
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        // Blueprint: action 1 routes to {2,3} (a parallel fan-out); actions 2,3 are terminal.
        var blueprint = new BlueprintModel
        {
            Id = BlueprintId,
            Title = "Routing Blueprint",
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Start",
                    Sender = "p1",
                    IsStartingAction = true,
                    Routes = [new RouteModel { NextActionIds = [2, 3] }],
                },
                new ActionModel { Id = 2, Title = "Branch A", Sender = "p1" },
                new ActionModel { Id = 3, Title = "Branch B", Sender = "p1" },
            ],
        };
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Governance roster: null record unless a required strength is configured.
        AdminRoster? roster = requiredStrength is null
            ? null
            : new AdminRoster
            {
                RegisterId = RegisterId,
                ControlRecord = new RegisterControlRecord { RoutingAttestation = requiredStrength },
            };
        _rosterMock.Setup(r => r.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var config = Options.Create(new ValidationEngineConfiguration());
        return new ValidationEngine(
            config,
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object,
            governanceRosterService: _rosterMock.Object);
    }

    private void SetupSignatureVerification(CryptoStatus status)
        => _cryptoModuleMock.Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

    private static Transaction CreateActionTransaction(RoutingDecision decision, int completedActionId = 1)
    {
        return new Transaction
        {
            TransactionId = $"tx-{Guid.NewGuid():N}",
            RegisterId = RegisterId,
            BlueprintId = BlueprintId,
            ActionId = completedActionId.ToString(),
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = "prev-tx",
            Metadata = new Dictionary<string, string>
            {
                ["routingDecision"] = JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical),
            },
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
    }

    private static RoutingDecision Decision(params int[] nextActionIds) => new()
    {
        CompletedActionId = 1,
        NextActions = nextActionIds.Select(id => new ActionRef { ActionId = id }).ToList(),
        Attestation = new Attestation
        {
            Kind = AttestationKind.SenderSigned,
            Signature = Convert.ToBase64String(new byte[64]),
        },
    };

    // ---- VAL_ROUTING_001 (structural successor) ----

    [Fact]
    public async Task ValidateRoutingDecision_ValidSuccessor_Passes()
    {
        var engine = CreateEngine();
        SetupSignatureVerification(CryptoStatus.Success);
        var tx = CreateActionTransaction(Decision(2));

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRoutingDecision_ParallelBranches_BothPreserved()
    {
        var engine = CreateEngine();
        SetupSignatureVerification(CryptoStatus.Success);
        var tx = CreateActionTransaction(Decision(2, 3));

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRoutingDecision_NonSuccessor_RejectedWithRouting001()
    {
        var engine = CreateEngine();
        SetupSignatureVerification(CryptoStatus.Success);
        var tx = CreateActionTransaction(Decision(99));

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_ROUTING_001");
    }

    [Fact]
    public async Task ValidateRoutingDecision_TerminalEmptySet_Passes()
    {
        var engine = CreateEngine();
        SetupSignatureVerification(CryptoStatus.Success);
        var tx = CreateActionTransaction(Decision());

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRoutingDecision_CompletedActionMismatch_RejectedWithRouting001()
    {
        var engine = CreateEngine();
        SetupSignatureVerification(CryptoStatus.Success);
        // Decision says completedActionId=1 but tx is for action 2.
        var tx = CreateActionTransaction(Decision(2), completedActionId: 2);

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_ROUTING_001");
    }

    // ---- VAL_ROUTING_002 (attestation + governance) ----

    [Fact]
    public async Task ValidateRoutingDecision_ForgedSignature_RejectedWithRouting002()
    {
        var engine = CreateEngine();
        SetupSignatureVerification(CryptoStatus.InvalidSignature);
        var tx = CreateActionTransaction(Decision(2));

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_ROUTING_002");
    }

    [Fact]
    public async Task ValidateRoutingDecision_ReservedAttestationKind_RejectedWithRouting002()
    {
        var engine = CreateEngine();
        SetupSignatureVerification(CryptoStatus.Success);
        var decision = Decision(2);
        decision.Attestation!.Kind = AttestationKind.Proof;
        var tx = CreateActionTransaction(decision);

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_ROUTING_002");
    }

    [Fact]
    public async Task ValidateRoutingDecision_GovernanceRequiresStrongerStrength_RefusedInV1()
    {
        var engine = CreateEngine(requiredStrength: AttestationKind.ValidatorReEvaluated);
        SetupSignatureVerification(CryptoStatus.Success);
        var tx = CreateActionTransaction(Decision(2));

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_ROUTING_002");
    }

    [Fact]
    public async Task ValidateRoutingDecision_NoDecisionCarried_Passes()
    {
        var engine = CreateEngine();
        var tx = CreateActionTransaction(Decision(2));
        tx.Metadata.Remove("routingDecision");

        var result = await engine.ValidateRoutingDecisionAsync(tx);

        result.IsValid.Should().BeTrue();
    }
}
