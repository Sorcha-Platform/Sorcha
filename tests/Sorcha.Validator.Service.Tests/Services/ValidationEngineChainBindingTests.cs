// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

using Xunit;

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Tier 3 fallback for sender authorisation: when a participant has no hardcoded wallet on
/// the blueprint AND no published participant record on the register, the validator falls
/// back to walking the in-instance transaction chain to derive the late-binding from the
/// earliest prior transaction submitted by that participant. The signature on that earlier
/// transaction is the on-ledger proof of binding — no external state required.
/// Closes the gap that broke TradeFinance Action 6 on master after Feature 083 + SEC-AUDIT 4.8.
/// </summary>
public class ValidationEngineChainBindingTests
{
    private const string RegisterId = "reg-test";
    private const string BlueprintId = "bp-test";
    private const string InstanceId = "inst-test-0001";
    private const string ProcurementMgr = "procurement-mgr";
    private const string BoundWallet = "ws11q-procurement-bound-wallet";

    private readonly Mock<IBlueprintCache> _blueprintCacheMock = new();
    private readonly Mock<IHashProvider> _hashProviderMock = new();
    private readonly Mock<ICryptoModule> _cryptoModuleMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();
    private readonly Mock<IRightsEnforcementService> _rightsEnforcementMock = new();
    private readonly Mock<IWalletUtilities> _walletUtilitiesMock = new();
    private readonly Mock<ILogger<ValidationEngine>> _loggerMock = new();
    private readonly ValidationEngine _engine;

    public ValidationEngineChainBindingTests()
    {
        _registerClientMock.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 1 });

        _rightsEnforcementMock.Setup(r => r.ValidateGovernanceRightsAsync(
                It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) =>
                ValidationEngineResult.Success(tx.TransactionId, tx.RegisterId, TimeSpan.Zero));

        // Tier 1 + Tier 2 must both fail to reach Tier 3.
        _registerClientMock.Setup(r => r.ResolveParticipantAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishedParticipantRecord?)null);

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

        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBlueprint());

        SetupHashValidation();
    }

    [Fact]
    public async Task Tier3_PriorInstanceTxFromSameParticipant_AcceptsMatchingWallet()
    {
        // The walkthrough scenario: procurement-mgr is the open participant on Action 1
        // (starting action — validator skips strict check; binding happens via signature on
        // the recorded tx). Action 6 has procurement-mgr again. Tier 1 (no blueprint wallet)
        // and Tier 2 (no published record) both fail by design. Tier 3 walks the in-instance
        // chain, finds Action 1's tx with senderWallet == BoundWallet, and accepts the
        // matching wallet on Action 6.
        _registerClientMock.Setup(r => r.GetTransactionsByInstanceIdAsync(
                RegisterId, InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                BuildPriorTx(actionId: 1, senderWallet: BoundWallet, timestamp: DateTime.UtcNow.AddMinutes(-5))
            ]);

        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns(BoundWallet);

        var tx = BuildAction6Tx(senderWallet: BoundWallet);

        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        result.Errors.Should().NotContain(e => e.Code == "VAL_BP_002",
            "Tier 3 chain-derived binding should accept Action 6 when the wallet matches the earlier in-instance tx");
    }

    [Fact]
    public async Task Tier3_PriorInstanceTxWithDifferentWallet_RejectsAsImmutableBindingViolation()
    {
        // Late-binding is immutable per FR-004. If procurement-mgr's earlier tx was signed by
        // wallet A, a later tx by procurement-mgr signed by wallet B must be rejected — even
        // when neither wallet is on the blueprint or in published records.
        _registerClientMock.Setup(r => r.GetTransactionsByInstanceIdAsync(
                RegisterId, InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                BuildPriorTx(actionId: 1, senderWallet: BoundWallet, timestamp: DateTime.UtcNow.AddMinutes(-5))
            ]);

        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns("ws11q-attacker-wallet");

        var tx = BuildAction6Tx(senderWallet: "ws11q-attacker-wallet");

        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_002",
            "a different wallet for an already-bound late-bound participant must reject as immutable-binding violation");
    }

    [Fact]
    public async Task Tier3_NoPriorInstanceTxFromParticipant_FallsThroughToVAL_BP_002()
    {
        // Cold-start scenario: this is the first time procurement-mgr appears in the instance
        // chain. Tier 3 cannot derive a binding from no history; the existing fail-closed
        // VAL_BP_002 from SEC-AUDIT 4.8 still fires.
        _registerClientMock.Setup(r => r.GetTransactionsByInstanceIdAsync(
                RegisterId, InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns(BoundWallet);

        var tx = BuildAction6Tx(senderWallet: BoundWallet);

        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_002",
            "no prior chain binding means Tier 3 has nothing to authorise against");
    }

    [Fact]
    public async Task Tier3_MissingInstanceIdMetadata_FallsThroughToVAL_BP_002()
    {
        // Defensive: if the submission lacks an instanceId in metadata, Tier 3 has no chain
        // to consult and must keep the fail-closed behaviour.
        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns(BoundWallet);

        var tx = BuildAction6Tx(senderWallet: BoundWallet, withInstanceId: false);

        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_002");
        _registerClientMock.Verify(r => r.GetTransactionsByInstanceIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Tier 3 must not call the register when no instance id is available");
    }

    [Fact]
    public async Task Tier3_RegisterClientThrows_FallsThroughToVAL_BP_002_NotSilentPass()
    {
        // Security-relevant: a transient network blip in the chain-walk must NOT silently
        // admit a transaction that no Tier 1/2 record authorises. The catch returns null;
        // the caller falls through to the existing fail-closed VAL_BP_002.
        _registerClientMock.Setup(r => r.GetTransactionsByInstanceIdAsync(
                RegisterId, InstanceId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("register-service unreachable"));

        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns(BoundWallet);

        var tx = BuildAction6Tx(senderWallet: BoundWallet);

        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_002");
    }

    [Fact]
    public async Task Tier3_ParticipantRoleFilter_DoesNotLeakBindingAcrossParticipants()
    {
        // Participant A has prior history; participant B (also on the blueprint) has not
        // yet acted. A Tier 3 resolution for participant B must not accidentally return
        // participant A's wallet just because they share an instance.
        //
        // Here the current tx is Action 6 for procurement-mgr (B), but the only prior
        // in-instance tx is for sales-mgr (A). Tier 3 must find no match and fall through.
        var blueprint = new BlueprintModel
        {
            Id = BlueprintId,
            Title = "Two open participants",
            Participants =
            [
                new ParticipantModel { Id = ProcurementMgr, Name = "Procurement Manager" },
                new ParticipantModel { Id = "sales-mgr", Name = "Sales Manager" },
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1, Title = "Start", Sender = "sales-mgr", IsStartingAction = true,
                    Routes = [new RouteModel { NextActionIds = [6] }]
                },
                new ActionModel { Id = 6, Title = "Approve", Sender = ProcurementMgr }
            ]
        };
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _registerClientMock.Setup(r => r.GetTransactionsByInstanceIdAsync(
                RegisterId, InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                // Prior history exists — but for a DIFFERENT participant.
                BuildPriorTx(actionId: 1, senderWallet: "ws11q-sales-bound", timestamp: DateTime.UtcNow.AddMinutes(-5))
            ]);

        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns(BoundWallet);

        var tx = BuildAction6Tx(senderWallet: BoundWallet);

        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_002",
            "procurement-mgr has no prior in-instance tx of their own — sales-mgr's binding must not leak");
    }

    // ---------------- helpers ----------------

    private void SetupHashValidation()
    {
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(Convert.FromHexString("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
    }

    private static BlueprintModel BuildBlueprint() => new()
    {
        Id = BlueprintId,
        Title = "Procurement (open participant)",
        Participants =
        [
            // Open participant — no walletAddress; binding deferred to first submitter.
            new ParticipantModel { Id = ProcurementMgr, Name = "Procurement Manager" },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Raise PO",
                Sender = ProcurementMgr,
                IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [6] }]
            },
            new ActionModel
            {
                Id = 6,
                Title = "Approve Invoice",
                Sender = ProcurementMgr,
            }
        ]
    };

    private static Transaction BuildAction6Tx(string senderWallet, bool withInstanceId = true)
    {
        var metadata = new Dictionary<string, string>();
        if (withInstanceId)
        {
            metadata["instanceId"] = InstanceId;
        }
        return new Transaction
        {
            TransactionId = $"tx-{Guid.NewGuid():N}",
            RegisterId = RegisterId,
            BlueprintId = BlueprintId,
            ActionId = "6",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = "tx-prev-action-5",
            Metadata = metadata,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow,
                    SignedBy = senderWallet,
                }
            ]
        };
    }

    private static TransactionModel BuildPriorTx(uint actionId, string senderWallet, DateTime timestamp) => new()
    {
        TxId = $"tx-prior-{actionId:0000}-{Guid.NewGuid():N}",
        RegisterId = RegisterId,
        SenderWallet = senderWallet,
        TimeStamp = timestamp,
        MetaData = new TransactionMetaData
        {
            BlueprintId = BlueprintId,
            InstanceId = InstanceId,
            ActionId = actionId,
        },
        Signature = "stubbed-not-used-by-tier3"
    };
}
