// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Register.Models;
using System.Text.Json;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 174 / #1195 Phase 2 ("one assurance, two bindings") — async SorchaWallet path. A sealed
/// <c>PresentationOutcome</c> lifecycle transaction with kind=success for a required prior action
/// contributes its <c>verifiedClaims</c> into that action's reconstructed data under the reserved
/// <c>presentedCredential</c> key, so a later issuance action's claim mappings can resolve
/// <c>/presentedCredential/*</c> from the verified, sealed presentation. Guards: only kind=success
/// contributes; malformed/missing verifiedClaims contribute nothing (fail closed); when multiple
/// success outcomes exist the latest sealed wins; and the reserved key is STRIPPED from regular
/// action-tx payloads so a client can never smuggle a spoofed <c>presentedCredential</c> through a
/// prior action's submitted data.
/// </summary>
public class StateReconstructionPresentedCredentialTests
{
    private const string VerifiedGivenName = "Alice-from-sealed-outcome";
    private const string SpoofGivenName = "Attacker-via-prior-action-payload";

    private readonly Mock<IRegisterServiceClient> _mockRegisterClient = new();
    private readonly Mock<IWalletServiceClient> _mockWalletClient = new();
    private readonly Mock<Sorcha.Cryptography.Interfaces.ISymmetricCrypto> _mockSymmetricCrypto = new();
    private readonly StateReconstructionService _service;

    private const string InstanceId = "test-instance";
    private const string RegisterId = "test-register";
    private const string DelegationToken = "test-delegation-token";

    private static readonly Dictionary<string, string> ParticipantWallets = new()
    {
        ["citizen"] = "wallet-citizen",
        ["issuer"] = "wallet-issuer"
    };

    public StateReconstructionPresentedCredentialTests()
    {
        _service = new StateReconstructionService(
            _mockRegisterClient.Object,
            _mockWalletClient.Object,
            _mockSymmetricCrypto.Object,
            new Mock<ILogger<StateReconstructionService>>().Object);
    }

    /// <summary>Two-action blueprint mirroring the AIAS device-binding shape: gated action 1 → issuance action 2.</summary>
    private static BlueprintModel GatedThenIssueBlueprint() => new()
    {
        Id = "bp-device",
        Title = "Gate then issue",
        Participants =
        [
            new ParticipantModel { Id = "citizen", Name = "Citizen", WalletAddress = "wallet-citizen" },
            new ParticipantModel { Id = "issuer", Name = "Issuer", WalletAddress = "wallet-issuer" }
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1, Title = "Present credential", Sender = "citizen",
                Routes = [new RouteModel { NextActionIds = [2] }]
            },
            new ActionModel
            {
                Id = 2, Title = "Issue device-bound copy", Sender = "issuer",
                RequiredPriorActions = [1]
            }
        ]
    };

    private static TransactionModel OutcomeTx(
        string txId,
        DateTime timeStamp,
        string kind = "success",
        object? verifiedClaims = null,
        bool omitVerifiedClaims = false)
    {
        // Shape mirrors ITransactionBuilderService.BuildPresentationOutcomeAsync's plaintext
        // transactionPayload, as sealed by the validator (Payloads[0].Data = base64url(JSON)).
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "presentation-outcome",
            ["kind"] = kind,
            ["blueprintId"] = "bp-device",
            ["actionId"] = 1,
            ["instanceId"] = InstanceId,
            ["presentationRequestId"] = Guid.NewGuid(),
            ["consumerName"] = "SorchaWallet",
            ["submitterWallet"] = "wallet-citizen",
            ["timestamp"] = DateTimeOffset.UtcNow
        };
        if (!omitVerifiedClaims)
        {
            payload["verifiedClaims"] = verifiedClaims ?? new Dictionary<string, object> { ["givenName"] = VerifiedGivenName };
        }

        return new TransactionModel
        {
            TxId = txId,
            RegisterId = RegisterId,
            TimeStamp = timeStamp,
            MetaData = new TransactionMetaData
            {
                ActionId = 1,
                TransactionType = Sorcha.Register.Models.Enums.TransactionType.PresentationOutcome,
                TrackingData = new Dictionary<string, string> { ["outcomeKind"] = kind }
            },
            Payloads =
            [
                new PayloadModel
                {
                    Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)),
                    WalletAccess = Array.Empty<string>()
                }
            ]
        };
    }

    /// <summary>A regular DevMode-plaintext Action tx for action 1 carrying the given fields.</summary>
    private static TransactionModel DevModeActionTx(string txId, DateTime timeStamp, object fields)
    {
        var envelope = new
        {
            type = "action",
            blueprintId = "bp-device",
            actionId = 1,
            instanceId = InstanceId,
            payloads = new Dictionary<string, object> { ["wallet-citizen"] = fields }
        };
        return new TransactionModel
        {
            TxId = txId,
            RegisterId = RegisterId,
            TimeStamp = timeStamp,
            MetaData = new TransactionMetaData
            {
                ActionId = 1,
                TransactionType = Sorcha.Register.Models.Enums.TransactionType.Action
            },
            Payloads =
            [
                new PayloadModel
                {
                    Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(envelope)),
                    WalletAccess = Array.Empty<string>()
                }
            ]
        };
    }

    private void SetupRegister(List<TransactionModel> transactions, bool devMode = false)
    {
        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(RegisterId, InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);
        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = RegisterId, Name = "Test", DevMode = devMode });
    }

    private Task<Sorcha.Blueprint.Service.Models.AccumulatedState> ReconstructForIssuanceActionAsync() =>
        _service.ReconstructAsync(
            GatedThenIssueBlueprint(), InstanceId, currentActionId: 2, RegisterId,
            DelegationToken, ParticipantWallets);

    [Fact]
    public async Task ReconstructAsync_SuccessOutcomeForPriorAction_SurfacesVerifiedClaimsUnderPresentedCredential()
    {
        SetupRegister([OutcomeTx("tx-out-1", DateTime.UtcNow.AddMinutes(-5))]);

        var result = await ReconstructForIssuanceActionAsync();

        result.ActionData.Should().ContainKey("1",
            "the sealed success outcome for gated action 1 must contribute reconstructed data");
        result.ActionData["1"].GetProperty("presentedCredential").GetProperty("givenName").GetString()
            .Should().Be(VerifiedGivenName,
                "the verified presentation's disclosed claims must surface under the reserved presentedCredential key");
    }

    [Fact]
    public async Task ReconstructAsync_DeclineOutcome_ContributesNothing()
    {
        SetupRegister([OutcomeTx("tx-out-1", DateTime.UtcNow.AddMinutes(-5), kind: "decline")]);

        var result = await ReconstructForIssuanceActionAsync();

        result.ActionData.Should().NotContainKey("1", "only kind=success outcomes contribute verified claims");
    }

    [Fact]
    public async Task ReconstructAsync_SuccessOutcomeWithMissingVerifiedClaims_ContributesNothing()
    {
        SetupRegister([OutcomeTx("tx-out-1", DateTime.UtcNow.AddMinutes(-5), omitVerifiedClaims: true)]);

        var result = await ReconstructForIssuanceActionAsync();

        result.ActionData.Should().NotContainKey("1",
            "a success outcome with missing/malformed verifiedClaims must contribute nothing (fail closed)");
    }

    [Fact]
    public async Task ReconstructAsync_MultipleSuccessOutcomes_LatestSealedWins()
    {
        SetupRegister(
        [
            OutcomeTx("tx-out-1", DateTime.UtcNow.AddMinutes(-10),
                verifiedClaims: new Dictionary<string, object> { ["givenName"] = "Stale-earlier-outcome" }),
            OutcomeTx("tx-out-2", DateTime.UtcNow.AddMinutes(-5),
                verifiedClaims: new Dictionary<string, object> { ["givenName"] = VerifiedGivenName })
        ]);

        var result = await ReconstructForIssuanceActionAsync();

        result.ActionData["1"].GetProperty("presentedCredential").GetProperty("givenName").GetString()
            .Should().Be(VerifiedGivenName, "when multiple success outcomes exist the latest sealed wins");
    }

    [Fact]
    public async Task ReconstructAsync_ActionPayloadPresentedCredential_IsStrippedAndVerifiedOutcomeWins()
    {
        // SECURITY — a client that smuggles a `presentedCredential` field into a PRIOR action's
        // submitted payload must not have it surface as the trusted verified source at issuance.
        // The reserved key is stripped from regular action-tx data; only the sealed success
        // outcome may populate it.
        SetupRegister(
        [
            DevModeActionTx("tx-act-1", DateTime.UtcNow.AddMinutes(-10), new
            {
                deviceKey = new { holderJwk = "the-device-jwk" },
                presentedCredential = new { givenName = SpoofGivenName }
            }),
            OutcomeTx("tx-out-1", DateTime.UtcNow.AddMinutes(-5))
        ], devMode: true);

        var result = await ReconstructForIssuanceActionAsync();

        result.ActionData.Should().ContainKey("1");
        result.ActionData["1"].GetProperty("deviceKey").GetProperty("holderJwk").GetString()
            .Should().Be("the-device-jwk", "legitimate action-payload fields are preserved");
        result.ActionData["1"].GetProperty("presentedCredential").GetProperty("givenName").GetString()
            .Should().Be(VerifiedGivenName,
                "the payload-smuggled presentedCredential must be stripped and the sealed verified outcome must win");
    }

    [Fact]
    public async Task ReconstructAsync_ActionPayloadPresentedCredential_NoOutcome_IsStripped()
    {
        SetupRegister(
        [
            DevModeActionTx("tx-act-1", DateTime.UtcNow.AddMinutes(-10), new
            {
                deviceKey = new { holderJwk = "the-device-jwk" },
                presentedCredential = new { givenName = SpoofGivenName }
            })
        ], devMode: true);

        var result = await ReconstructForIssuanceActionAsync();

        result.ActionData.Should().ContainKey("1");
        result.ActionData["1"].TryGetProperty("presentedCredential", out _).Should().BeFalse(
            "with no verified outcome, a payload-smuggled presentedCredential is stripped and never surfaces (fail closed)");
    }
}
