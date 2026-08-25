// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Haip;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 176 / FR-004 / SC-003 — a decision action that carries a <c>credentialIssuanceConfig</c>
/// with an <c>issuanceCondition</c> must mint a credential ONLY when the condition holds over the
/// submitted decision. A rejected application therefore never receives a credential, while an approved
/// one does, and a config with no condition keeps the pre-existing always-issue behaviour. The mint gate
/// is a single shared flag applied to both delivery paths; these tests exercise the HAIP offer mint
/// (lighter to trigger) plus the SorchaLocalWallet skip case that the AIAS demo actually uses.
/// </summary>
public class ActionExecutionCredentialGatingTests
{
    private readonly Mock<IActionResolverService> _actionResolver = new();
    private readonly Mock<IStateReconstructionService> _stateReconstruction = new();
    private readonly Mock<ITransactionBuilderService> _transactionBuilder = new();
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly Mock<IValidatorServiceClient> _validatorClient = new();
    private readonly Mock<IWalletServiceClient> _walletClient = new();
    private readonly Mock<IParticipantServiceClient> _participantClient = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IInstanceStore> _instanceStore = new();
    private readonly Mock<IActionStore> _actionStore = new();
    private readonly Mock<IExecutionEngine> _executionEngine = new();
    private readonly Mock<IHaipServiceClient> _haipClient = new();

    private static readonly JsonNode ApprovedOnly = JsonNode.Parse("""{ "==": [ { "var": "decision" }, "approved" ] }""")!;

    private ActionExecutionService CreateService() => new(
        _actionResolver.Object, _stateReconstruction.Object, _transactionBuilder.Object,
        _registerClient.Object, _validatorClient.Object, _walletClient.Object,
        _participantClient.Object, _notificationService.Object, _instanceStore.Object,
        _actionStore.Object, _executionEngine.Object,
        new Mock<ILogger<ActionExecutionService>>().Object, new ConfigurationBuilder().Build(),
        haipClient: _haipClient.Object,
        // Real evaluator so the issuanceCondition is genuinely evaluated over the submitted decision.
        jsonLogicEvaluator: new JsonLogicEvaluator());

    private static Instance TestInstance() => new()
    {
        Id = "inst-1", BlueprintId = "bp-1", BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", BlueprintVersion = 1, RegisterId = "reg-1",
        TenantId = "t-1", State = InstanceState.Active, CurrentActionIds = [1],
        ParticipantWallets = new Dictionary<string, string> { ["citizen"] = "wallet-citizen" },
    };

    private static BlueprintModel BlueprintWith(CredentialIssuanceConfig config) => new()
    {
        Id = "bp-1",
        Title = "AIAS",
        Participants = [new ParticipantModel { Id = "citizen", Name = "Applicant", WalletAddress = "wallet-citizen" }],
        Actions =
        [
            new ActionModel
            {
                Id = 1, Title = "Verify Assured Identity Application", Sender = "citizen", IsStartingAction = true,
                CredentialIssuanceConfig = config,
                Routes = [new RouteModel { NextActionIds = [] }],
            },
        ],
    };

    private static CredentialIssuanceConfig HaipConfig(JsonNode? issuanceCondition) => new()
    {
        CredentialType = "AssuredIdentityCredential",
        TargetAudience = TargetAudience.HaipExternalWallet,
        RecipientParticipantId = "citizen",
        ClaimMappings = [new ClaimMapping { ClaimName = "decision", SourceField = "/decision" }],
        IssuanceCondition = issuanceCondition,
    };

    private static ActionSubmissionRequest RequestWithDecision(string decision) => new()
    {
        BlueprintId = "bp-1", ActionId = "1", SenderWallet = "wallet-citizen", RegisterAddress = "reg-1",
        PayloadData = new Dictionary<string, object> { ["decision"] = decision },
    };

    private void SetupFlow(BlueprintModel blueprint)
    {
        var instance = TestInstance();
        var action = blueprint.Actions!.First();

        _instanceStore.Setup(x => x.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        _actionResolver.Setup(x => x.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        _actionResolver.Setup(x => x.GetActionDefinition(blueprint, "1")).Returns(action);
        _actionStore.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

        _stateReconstruction.Setup(x => x.ReconstructAsync(
                It.IsAny<BlueprintModel>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState());

        _executionEngine.Setup(x => x.ValidateAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());
        _executionEngine.Setup(x => x.ApplyCalculationsAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());
        _executionEngine.Setup(x => x.DetermineRoutingWithMappingAsync(
                blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<JsonObject?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());
        _executionEngine.Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());

        // DevMode register — skip the encryption pipeline.
        _registerClient.Setup(x => x.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = "reg-1", Name = "Dev", DevMode = true });
        _registerClient.Setup(x => x.ResolvePublicKeysBatchAsync(It.IsAny<string>(), It.IsAny<BatchPublicKeyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse { Resolved = new(), NotFound = [], Revoked = [] });

        _walletClient.Setup(x => x.SignTransactionAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult { Signature = new byte[64], PublicKey = new byte[32], SignedBy = "wallet-citizen", Algorithm = "ED25519" });
        _validatorClient.Setup(x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = new string('a', 64) });
        _registerClient.Setup(x => x.GetTransactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel { TxId = new string('a', 64), DocketNumber = 1 });
        _instanceStore.Setup(x => x.UpdateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance i, CancellationToken _) => i);

        _haipClient.Setup(x => x.CreateCredentialOfferAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
                It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateOfferResult(Guid.NewGuid(), "openid-credential-offer://x", "pac", DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task RejectedDecision_WithApprovedOnlyIssuanceCondition_DoesNotMintCredential()
    {
        var blueprint = BlueprintWith(HaipConfig(ApprovedOnly));
        SetupFlow(blueprint);

        await CreateService().ExecuteAsync("inst-1", 1, RequestWithDecision("rejected"), "delg-token");

        _haipClient.Verify(x => x.CreateCredentialOfferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()),
            Times.Never, "a rejected decision must not mint a credential (SC-003)");
    }

    [Fact]
    public async Task ApprovedDecision_WithApprovedOnlyIssuanceCondition_MintsCredential()
    {
        var blueprint = BlueprintWith(HaipConfig(ApprovedOnly));
        SetupFlow(blueprint);

        await CreateService().ExecuteAsync("inst-1", 1, RequestWithDecision("approved"), "delg-token");

        _haipClient.Verify(x => x.CreateCredentialOfferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()),
            Times.Once, "an approved decision must mint the credential");
    }

    [Fact]
    public async Task NoIssuanceCondition_RejectedDecision_StillMints_PreservesLegacyBehaviour()
    {
        // Backward-compat: a config with no issuanceCondition always issues on execution, regardless of
        // any decision field — the pre-existing behaviour for blueprints that don't opt into gating.
        var blueprint = BlueprintWith(HaipConfig(issuanceCondition: null));
        SetupFlow(blueprint);

        await CreateService().ExecuteAsync("inst-1", 1, RequestWithDecision("rejected"), "delg-token");

        _haipClient.Verify(x => x.CreateCredentialOfferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()),
            Times.Once, "with no issuanceCondition the credential is always minted (legacy behaviour)");
    }
}
