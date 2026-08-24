// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Haip;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Blueprint.Engine.Credentials;
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
/// Feature 174 / #1195 Phase 2 ("one assurance, two bindings") — the verified presentation's
/// disclosed claims from an action's credential-requirement gate must be exposed in the issuance
/// source document under the stable <c>/presentedCredential/*</c> claim-source prefix, and they
/// MUST take precedence over client-supplied payload for those claims. This is the SECURITY
/// property of design §4.1: the device-bound copy's identity claims come from the verified,
/// issuer-signed root presentation (tamper-evident) — never from client-supplied payload. A
/// submitted payload that names <c>givenName</c> (or even a spoofed <c>presentedCredential</c>
/// object) can never override the value disclosed by the verified presentation.
/// </summary>
public class ActionExecutionPresentedCredentialSourceTests
{
    private const string PresentedGivenName = "Alice-from-verified-presentation";
    private const string PayloadSpoofGivenName = "Attacker-supplied-payload";
    private const string CredentialType = "https://sorcha.dev/vc/assured-identity/v1";

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
    private readonly Mock<ICredentialVerifier> _credentialVerifier = new();

    // Captures the claims dictionary the HAIP path builds from claimMappings + the issuance source doc.
    private Dictionary<string, object>? _capturedOfferClaims;

    private ActionExecutionService CreateService() => new(
        _actionResolver.Object, _stateReconstruction.Object, _transactionBuilder.Object,
        _registerClient.Object, _validatorClient.Object, _walletClient.Object,
        _participantClient.Object, _notificationService.Object, _instanceStore.Object,
        _actionStore.Object, _executionEngine.Object,
        new Mock<ILogger<ActionExecutionService>>().Object, new ConfigurationBuilder().Build(),
        credentialVerifier: _credentialVerifier.Object,
        haipClient: _haipClient.Object,
        jsonLogicEvaluator: new JsonLogicEvaluator());

    private static Instance TestInstance() => new()
    {
        Id = "inst-1", BlueprintId = "bp-1", BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", BlueprintVersion = 1, RegisterId = "reg-1",
        TenantId = "t-1", State = InstanceState.Active, CurrentActionIds = [1],
        ParticipantWallets = new Dictionary<string, string> { ["citizen"] = "wallet-citizen" },
    };

    // A single action that BOTH gates on presenting a credential AND issues one whose givenName is
    // mapped from the verified presentation via the /presentedCredential/* prefix.
    private static BlueprintModel GateAndIssueBlueprint() => new()
    {
        Id = "bp-1",
        Title = "AIAS device binding (single-action test shape)",
        Participants = [new ParticipantModel { Id = "citizen", Name = "Holder", WalletAddress = "wallet-citizen" }],
        Actions =
        [
            new ActionModel
            {
                Id = 1, Title = "Bind identity to device", Sender = "citizen", IsStartingAction = true,
                CredentialRequirements =
                [
                    new CredentialRequirement
                    {
                        Type = CredentialType,
                        PresentationSource = PresentationSource.SorchaInternal,
                        RequiredClaims = [new ClaimConstraint { ClaimName = "givenName" }],
                    },
                ],
                CredentialIssuanceConfig = new CredentialIssuanceConfig
                {
                    CredentialType = "AssuredIdentityCredential",
                    Vct = CredentialType,
                    TargetAudience = TargetAudience.HaipExternalWallet,
                    RecipientParticipantId = "citizen",
                    ClaimMappings =
                    [
                        new ClaimMapping { ClaimName = "givenName", SourceField = "/presentedCredential/givenName" },
                    ],
                },
                Routes = [new RouteModel { NextActionIds = [] }],
            },
        ],
    };

    private static ActionSubmissionRequest RequestWithPayloadSpoof() => new()
    {
        BlueprintId = "bp-1", ActionId = "1", SenderWallet = "wallet-citizen", RegisterAddress = "reg-1",
        // A submitted presentation satisfies the internal verifier gate (the mock returns the verified claims).
        CredentialPresentations =
        [
            new CredentialPresentation
            {
                CredentialId = "did:sorcha:credential:root",
                RawPresentation = "eyJ.presentation.token",
                DisclosedClaims = new Dictionary<string, object> { ["givenName"] = PresentedGivenName },
            },
        ],
        // The client tries to override the identity claim two ways: a root givenName AND a spoofed
        // presentedCredential object. Neither may win.
        PayloadData = new Dictionary<string, object>
        {
            ["givenName"] = PayloadSpoofGivenName,
            ["presentedCredential"] = new Dictionary<string, object> { ["givenName"] = PayloadSpoofGivenName },
        },
    };

    private void SetupFlow(BlueprintModel blueprint)
    {
        var instance = TestInstance();
        var action = blueprint.Actions!.First();

        _instanceStore.Setup(x => x.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        _actionResolver.Setup(x => x.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        _actionResolver.Setup(x => x.GetActionDefinition(blueprint, "1")).Returns(action);
        _actionStore.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

        // The verified presentation discloses givenName — this is the authoritative source.
        _credentialVerifier.Setup(x => x.VerifyAsync(
                It.IsAny<IEnumerable<CredentialRequirement>>(),
                It.IsAny<IEnumerable<CredentialPresentation>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialValidationResult
            {
                IsValid = true,
                VerifiedCredentials =
                [
                    new VerifiedCredentialDetail
                    {
                        CredentialId = "did:sorcha:credential:root",
                        Type = CredentialType,
                        IssuerDid = "did:sorcha:aias",
                        SignatureValid = true,
                        VerifiedClaims = new Dictionary<string, object> { ["givenName"] = PresentedGivenName },
                    },
                ],
            });

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
            .Callback<string, string, string, Dictionary<string, object>, List<string>?, CancellationToken>(
                (_, _, _, claims, _, _) => _capturedOfferClaims = claims)
            .ReturnsAsync(new CreateOfferResult(Guid.NewGuid(), "openid-credential-offer://x", "pac", DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task ExecuteAsync_ClaimMappingSourcesPresentedCredential_UsesVerifiedValueNotPayload()
    {
        var blueprint = GateAndIssueBlueprint();
        SetupFlow(blueprint);

        await CreateService().ExecuteAsync("inst-1", 1, RequestWithPayloadSpoof(), "delg-token");

        _capturedOfferClaims.Should().NotBeNull(
            "the HAIP issuance path must have built claims from the /presentedCredential/* source");
        _capturedOfferClaims!.Should().ContainKey("givenName");
        _capturedOfferClaims["givenName"].Should().Be(
            PresentedGivenName,
            "the issued givenName MUST come from the verified, issuer-signed presentation (design §4.1) — " +
            "a client-supplied payload value can never override an identity claim sourced from the verified presentation");
        _capturedOfferClaims["givenName"].Should().NotBe(
            PayloadSpoofGivenName, "the payload spoof must not win");
    }

    [Fact]
    public async Task ExecuteAsync_PresentedCredentialFromReconstructedPriorAction_FeedsIssuanceOverPayload()
    {
        // Async SorchaWallet path (two-action shape): the gated prior action's verified claims arrive
        // at the issuance action via state reconstruction (StateReconstructionService surfaces the
        // sealed PresentationOutcome's verifiedClaims under the reserved presentedCredential key).
        // The issuance action has NO synchronous gate of its own — the reconstructed source must feed
        // the claim mapping and beat a payload spoof.
        var blueprint = GateAndIssueBlueprint();
        // Remove the synchronous gate from the executing action — claims come from reconstruction only.
        blueprint.Actions!.First().CredentialRequirements = null;
        SetupFlow(blueprint);

        // Reconstructed prior-action data carries the verified presentation under presentedCredential.
        var priorActionData = JsonSerializer.Deserialize<JsonElement>(
            $$"""{ "presentedCredential": { "givenName": "{{PresentedGivenName}}" } }""");
        _stateReconstruction.Setup(x => x.ReconstructAsync(
                It.IsAny<BlueprintModel>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState
            {
                ActionData = new Dictionary<string, JsonElement> { ["1"] = priorActionData }
            });

        var request = new ActionSubmissionRequest
        {
            BlueprintId = "bp-1", ActionId = "1", SenderWallet = "wallet-citizen", RegisterAddress = "reg-1",
            PayloadData = new Dictionary<string, object>
            {
                ["presentedCredential"] = new Dictionary<string, object> { ["givenName"] = PayloadSpoofGivenName },
            },
        };

        await CreateService().ExecuteAsync("inst-1", 1, request, "delg-token");

        _capturedOfferClaims.Should().NotBeNull();
        _capturedOfferClaims!.Should().ContainKey("givenName");
        _capturedOfferClaims["givenName"]!.ToString().Should().Be(
            PresentedGivenName,
            "the reconstructed verified presentation (sealed outcome tx) must feed the issuance claim, " +
            "and a payload spoof must never override it");
    }
}
