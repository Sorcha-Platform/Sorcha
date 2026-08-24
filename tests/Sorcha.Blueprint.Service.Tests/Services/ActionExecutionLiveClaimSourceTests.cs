// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.PlatformUserClaims;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
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
/// Guards issue #1264. An <c>x-claim-source</c> binding must be resolved from LIVE server state at
/// submission and must overwrite whatever the client sent.
/// <para>
/// Feature 183 US1 seeded these bindings client-side from the browser's JWT, so the value was only
/// ever as fresh as the token the client happened to hold. A citizen's token was minted at signup
/// carrying <c>email_verified: false</c>; they verified nine minutes later; the application they
/// submitted five minutes after that was auto-rejected on the stale <c>false</c> — while fully
/// compliant. Verifying updates server state but cannot rewrite an issued token, and nothing re-mints
/// it, so this hit any citizen who verified mid-session: the normal signup order.
/// </para>
/// <para>
/// Two properties are asserted here, and both matter because the field gates an identity decision:
/// the live value wins over a <b>stale</b> client value (the reported bug), and it equally wins over a
/// <b>favourable</b> client value (so a client cannot assert a field the platform vouches for).
/// </para>
/// </summary>
public class ActionExecutionLiveClaimSourceTests
{
    private static readonly Guid PlatformUserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

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
    private readonly Mock<IPlatformUserClaimsClient> _platformUserClaims = new();

    /// <summary>The payload as it reached validation — i.e. what gets signed and sealed.</summary>
    private Dictionary<string, object>? _validatedPayload;

    private ActionExecutionService CreateService() => new(
        _actionResolver.Object, _stateReconstruction.Object, _transactionBuilder.Object,
        _registerClient.Object, _validatorClient.Object, _walletClient.Object,
        _participantClient.Object, _notificationService.Object, _instanceStore.Object,
        _actionStore.Object, _executionEngine.Object,
        new Mock<ILogger<ActionExecutionService>>().Object, new ConfigurationBuilder().Build(),
        jsonLogicEvaluator: new JsonLogicEvaluator(),
        platformUserClaims: _platformUserClaims.Object);

    /// <summary>A consumer-tier citizen principal, as F136 mints it.</summary>
    private static ClaimsPrincipal Citizen(bool includePlatformUserId = true, string tokenEmailVerified = "false")
    {
        var claims = new List<Claim>
        {
            // Must parse as a Guid — ValidateWalletOwnershipAsync rejects otherwise. org_id is
            // deliberately omitted so that check skips participant linkage and these tests stay
            // focused on claim-source resolution.
            new("sub", "99999999-8888-7777-6666-555555555555"),
            new("email", "citizen@example.test"),
            // The token's OWN copy of the claim is deliberately the stale value from the bug report.
            // Nothing may fall back to it.
            new("email_verified", tokenEmailVerified),
        };
        if (includePlatformUserId)
        {
            claims.Add(new Claim("platform_user_id", PlatformUserId.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static Instance TestInstance() => new()
    {
        Id = "inst-1", BlueprintId = "bp-1", BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", BlueprintVersion = 1, RegisterId = "reg-1",
        TenantId = "t-1", State = InstanceState.Active, CurrentActionIds = [1],
        ParticipantWallets = new Dictionary<string, string> { ["citizen"] = "wallet-citizen" },
    };

    /// <summary>The AIAS action-1 shape: a headless, read-only emailVerified field bound to the claim.</summary>
    private static BlueprintModel BlueprintWithClaimSource() => new()
    {
        Id = "bp-1",
        Title = "AIAS Assured Identity (claim-source test shape)",
        Participants = [new ParticipantModel { Id = "citizen", Name = "Applicant", WalletAddress = "wallet-citizen" }],
        Actions =
        [
            new ActionModel
            {
                Id = 1, Title = "Apply", Sender = "citizen", IsStartingAction = true,
                DataSchemas =
                [
                    JsonDocument.Parse("""
                        {
                          "type": "object",
                          "properties": {
                            "name": { "type": "string" },
                            "emailVerified": {
                              "type": "boolean",
                              "readOnly": true,
                              "x-claim-source": "email_verified"
                            }
                          }
                        }
                        """),
                ],
                Routes = [new RouteModel { NextActionIds = [] }],
            },
        ],
    };

    private static BlueprintModel BlueprintWithoutClaimSource()
    {
        var blueprint = BlueprintWithClaimSource();
        blueprint.Actions!.First().DataSchemas =
        [
            JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}"""),
        ];
        return blueprint;
    }

    private static ActionSubmissionRequest RequestWith(object? clientEmailVerified)
    {
        var payload = new Dictionary<string, object> { ["name"] = "Stuart Fraser" };
        if (clientEmailVerified is not null)
        {
            payload["emailVerified"] = clientEmailVerified;
        }
        return new ActionSubmissionRequest
        {
            BlueprintId = "bp-1", ActionId = "1", SenderWallet = "wallet-citizen",
            RegisterAddress = "reg-1", PayloadData = payload,
        };
    }

    private void SetupFlow(BlueprintModel blueprint)
    {
        var action = blueprint.Actions!.First();

        _instanceStore.Setup(x => x.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(TestInstance());
        _actionResolver.Setup(x => x.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        _actionResolver.Setup(x => x.GetActionDefinition(blueprint, "1")).Returns(action);
        _actionStore.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

        _stateReconstruction.Setup(x => x.ReconstructAsync(
                It.IsAny<BlueprintModel>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState());

        // Capture the payload exactly as it reaches validation: injection runs before this, and this
        // same payload is what the signed transaction is built from.
        _executionEngine.Setup(x => x.ValidateAsync(
                It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .Callback<Dictionary<string, object>, ActionModel, CancellationToken>(
                (data, _, _) => _validatedPayload = new Dictionary<string, object>(data))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());
        _executionEngine.Setup(x => x.ApplyCalculationsAsync(
                It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());
        _executionEngine.Setup(x => x.DetermineRoutingWithMappingAsync(
                blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<JsonObject?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());
        _executionEngine.Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());

        _registerClient.Setup(x => x.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = "reg-1", Name = "Dev", DevMode = true });
        _registerClient.Setup(x => x.ResolvePublicKeysBatchAsync(
                It.IsAny<string>(), It.IsAny<BatchPublicKeyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse { Resolved = new(), NotFound = [], Revoked = [] });
        _walletClient.Setup(x => x.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64], PublicKey = new byte[32],
                SignedBy = "wallet-citizen", Algorithm = "ED25519",
            });
        _validatorClient.Setup(x => x.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = new string('a', 64) });
        _registerClient.Setup(x => x.GetTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel { TxId = new string('a', 64), DocketNumber = 1 });
        _instanceStore.Setup(x => x.UpdateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance i, CancellationToken _) => i);
    }

    private void LiveEmailVerified(string value)
        => _platformUserClaims
            .Setup(x => x.ResolveAsync(PlatformUserId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["email_verified"] = value });

    /// <summary>
    /// THE reported bug: the client sends the stale <c>false</c> its token carried, server state says
    /// verified. The submission must carry <c>true</c>.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_LiveClaimVerified_OverridesStaleClientFalse()
    {
        SetupFlow(BlueprintWithClaimSource());
        LiveEmailVerified("true");

        await CreateService().ExecuteAsync("inst-1", 1, RequestWith(false), "delg-token", Citizen());

        _validatedPayload.Should().NotBeNull();
        _validatedPayload!["emailVerified"].Should().Be(
            true,
            "the citizen HAD verified — a stale token value must never decide an identity gate (#1264)");
    }

    /// <summary>
    /// The mirror property: a client claiming verified when server state says otherwise must not get
    /// it. Without this the fix would trade a false-reject for a false-accept.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_LiveClaimUnverified_OverridesFavourableClientTrue()
    {
        SetupFlow(BlueprintWithClaimSource());
        LiveEmailVerified("false");

        await CreateService().ExecuteAsync("inst-1", 1, RequestWith(true), "delg-token", Citizen());

        _validatedPayload!["emailVerified"].Should().Be(
            false, "a client must not be able to assert a field the platform is supposed to vouch for");
    }

    /// <summary>The value is stamped even when the client omits the field entirely.</summary>
    [Fact]
    public async Task ExecuteAsync_ClientOmitsTheField_ServerStillStampsIt()
    {
        SetupFlow(BlueprintWithClaimSource());
        LiveEmailVerified("true");

        await CreateService().ExecuteAsync("inst-1", 1, RequestWith(null), "delg-token", Citizen());

        _validatedPayload!.Should().ContainKey("emailVerified").WhoseValue.Should().Be(true);
    }

    /// <summary>Untouched fields are untouched — injection must not disturb the rest of the payload.</summary>
    [Fact]
    public async Task ExecuteAsync_LeavesOtherPayloadFieldsAlone()
    {
        SetupFlow(BlueprintWithClaimSource());
        LiveEmailVerified("true");

        await CreateService().ExecuteAsync("inst-1", 1, RequestWith(false), "delg-token", Citizen());

        _validatedPayload!["name"].Should().Be("Stuart Fraser");
    }

    /// <summary>
    /// A binding the platform does not resolve fails closed — a boolean gate lands on a definite deny
    /// rather than an absent field whose meaning depends on the consumer.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ClaimNotResolvedByServer_FailsClosedToFalse()
    {
        SetupFlow(BlueprintWithClaimSource());
        _platformUserClaims
            .Setup(x => x.ResolveAsync(PlatformUserId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>()); // resolved nothing

        await CreateService().ExecuteAsync("inst-1", 1, RequestWith(true), "delg-token", Citizen());

        _validatedPayload!["emailVerified"].Should().Be(false);
    }

    /// <summary>
    /// A transient lookup failure must FAIL THE SUBMISSION, not sign a defaulted value. Signing
    /// <c>false</c> would write an irreversible wrongful rejection onto the ledger for a transient
    /// reason; a failed submission is recoverable because the citizen simply retries.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_LiveLookupUnavailable_FailsTheSubmission_AndSubmitsNothing()
    {
        SetupFlow(BlueprintWithClaimSource());
        _platformUserClaims
            .Setup(x => x.ResolveAsync(PlatformUserId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlatformUserClaimsUnavailableException("tenant unreachable"));

        var act = async () => await CreateService()
            .ExecuteAsync("inst-1", 1, RequestWith(false), "delg-token", Citizen());

        await act.Should().ThrowAsync<InvalidOperationException>();

        _validatorClient.Verify(x => x.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "nothing may be written to the ledger when the vouched-for value is unknown");
    }

    /// <summary>
    /// No resolvable platform user means no way to vouch for the value, so the submission is refused
    /// rather than falling back to the token's own copy — that fallback IS the bug.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CallerHasNoPlatformUserId_FailsTheSubmission()
    {
        SetupFlow(BlueprintWithClaimSource());
        LiveEmailVerified("true");

        var act = async () => await CreateService().ExecuteAsync(
            "inst-1", 1, RequestWith(false), "delg-token", Citizen(includePlatformUserId: false));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _validatorClient.Verify(x => x.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// No regression for the overwhelming majority of actions: an action declaring no binding must not
    /// make a claims call at all, so nothing new can fail for them.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ActionWithNoClaimSource_NeverCallsTheClaimsService()
    {
        SetupFlow(BlueprintWithoutClaimSource());

        await CreateService().ExecuteAsync("inst-1", 1, RequestWith(null), "delg-token", Citizen());

        _platformUserClaims.Verify(x => x.ResolveAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Only the names the schema actually declares are requested — the read states its intent rather
    /// than pulling everything the token would carry.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RequestsOnlyTheDeclaredClaimNames()
    {
        SetupFlow(BlueprintWithClaimSource());
        LiveEmailVerified("true");
        IReadOnlyCollection<string>? requested = null;
        _platformUserClaims
            .Setup(x => x.ResolveAsync(PlatformUserId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<string>, CancellationToken>((_, names, _) => requested = names)
            .ReturnsAsync(new Dictionary<string, string> { ["email_verified"] = "true" });

        await CreateService().ExecuteAsync("inst-1", 1, RequestWith(false), "delg-token", Citizen());

        requested.Should().BeEquivalentTo(new[] { "email_verified" });
    }
}
