// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Blueprint.Models.Canonical;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 197 (T014): a transaction on an ordinary register may be pinned to a SYSTEM blueprint
/// publication that lives only on the system register (SSR). The validator must resolve it from the
/// SSR, verifying the payload against the SSR-scoped publication id — and must treat a tampered
/// payload as a refusal, never as a reason to look elsewhere. Reached through
/// <see cref="ValidationEngine.ValidateRoutingDecisionAsync"/>, the smallest public path that
/// resolves a pinned definition: an unresolvable pin surfaces as <c>VAL_BP_VERSION_001</c>.
/// </summary>
public class ValidationEngineSystemRegisterPinTests
{
    private const string OwnRegister = "own-register";
    private const string SystemRegister = SystemRegisterConstants.SystemRegisterId;
    private const string BlueprintId = "bp-system";

    private readonly Mock<IBlueprintCache> _cache = new();
    private readonly Mock<IRegisterServiceClient> _registerClient = new();

    private static string DefinitionJson() => JsonSerializer.Serialize(new BlueprintModel
    {
        Id = BlueprintId,
        Title = "System Blueprint",
        Actions =
        [
            new ActionModel
            {
                Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2] }],
            },
            new ActionModel { Id = 2, Title = "Next", Sender = "p1" },
        ],
    });

    private static string PublicationIdOn(string register, string json)
        => BlueprintPublicationId.ComputeFromDefinition(register, BlueprintId, json);

    private static TransactionModel PublicationTx(string registerId, string txId, string json) => new()
    {
        RegisterId = registerId,
        TxId = txId,
        Payloads =
        [
            new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) },
        ],
    };

    private ValidationEngine CreateEngine()
    {
        var hash = new Mock<IHashProvider>();
        hash.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256)).Returns(new byte[32]);
        var crypto = new Mock<ICryptoModule>();
        crypto.Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);

        return new ValidationEngine(
            Options.Create(new ValidationEngineConfiguration()),
            _cache.Object,
            hash.Object,
            crypto.Object,
            Mock.Of<IWalletUtilities>(),
            _registerClient.Object,
            Mock.Of<IRightsEnforcementService>(),
            Mock.Of<ILogger<ValidationEngine>>(),
            governanceRosterService: Mock.Of<IGovernanceRosterService>());
    }

    private static Transaction PinnedTransaction(string pin) => new()
    {
        TransactionId = $"tx-{Guid.NewGuid():N}",
        RegisterId = OwnRegister,
        BlueprintId = BlueprintId,
        ActionId = "1",
        Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
        PayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        CreatedAt = DateTimeOffset.UtcNow,
        PreviousTransactionId = "prev-tx",
        Metadata = new Dictionary<string, string>
        {
            ["routingDecision"] = JsonSerializer.Serialize(new RoutingDecision
            {
                CompletedActionId = 1,
                BlueprintDefinitionTxId = pin,
                NextActions = [new ActionRef { ActionId = 2 }],
                Attestation = new Attestation
                {
                    Kind = AttestationKind.SenderSigned,
                    Signature = Convert.ToBase64String(new byte[64]),
                },
            }, RegisterSerializationOptions.Canonical),
        },
        Signatures =
        [
            new RegisterSignature
            {
                PublicKey = new byte[32], SignatureValue = new byte[64],
                Algorithm = "ED25519", SignedAt = DateTimeOffset.UtcNow,
            },
        ],
    };

    [Fact]
    public async Task ValidateRoutingDecision_PinnedDefinitionOnlyOnSystemRegister_Resolves()
    {
        var json = DefinitionJson();
        var pin = PublicationIdOn(SystemRegister, json);
        _registerClient.Setup(r => r.GetTransactionAsync(SystemRegister, pin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicationTx(SystemRegister, pin, json));

        var result = await CreateEngine().ValidateRoutingDecisionAsync(PinnedTransaction(pin));

        result.Errors.Should().NotContain(e => e.Code == "VAL_BP_VERSION_001");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRoutingDecision_SystemRegisterPayloadTampered_RefusedAsUnresolvable()
    {
        var json = DefinitionJson();
        var pin = PublicationIdOn(SystemRegister, json);
        var tampered = json.Replace("System Blueprint", "Evil Blueprint");
        _registerClient.Setup(r => r.GetTransactionAsync(SystemRegister, pin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicationTx(SystemRegister, pin, tampered));

        var result = await CreateEngine().ValidateRoutingDecisionAsync(PinnedTransaction(pin));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
    }

    [Fact]
    public async Task ValidateRoutingDecision_PinnedDefinitionOnOwnRegister_SystemRegisterNeverQueried()
    {
        var json = DefinitionJson();
        var pin = PublicationIdOn(OwnRegister, json);
        _registerClient.Setup(r => r.GetTransactionAsync(OwnRegister, pin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicationTx(OwnRegister, pin, json));

        var result = await CreateEngine().ValidateRoutingDecisionAsync(PinnedTransaction(pin));

        result.IsValid.Should().BeTrue();
        _registerClient.Verify(r => r.GetTransactionAsync(
            SystemRegister, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateRoutingDecision_OwnRegisterPayloadTampered_DoesNotFallThroughToSystemRegister()
    {
        var json = DefinitionJson();
        var pin = PublicationIdOn(OwnRegister, json);
        _registerClient.Setup(r => r.GetTransactionAsync(OwnRegister, pin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicationTx(OwnRegister, pin, json.Replace("System Blueprint", "Evil Blueprint")));

        var result = await CreateEngine().ValidateRoutingDecisionAsync(PinnedTransaction(pin));

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
        _registerClient.Verify(r => r.GetTransactionAsync(
            SystemRegister, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateRoutingDecision_DefinitionOnNeitherRegister_RefusedAsUnresolvable()
    {
        var pin = PublicationIdOn(SystemRegister, DefinitionJson());

        var result = await CreateEngine().ValidateRoutingDecisionAsync(PinnedTransaction(pin));

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
    }
}
