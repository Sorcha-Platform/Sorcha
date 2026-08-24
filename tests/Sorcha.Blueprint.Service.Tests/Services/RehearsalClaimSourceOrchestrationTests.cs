// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Blueprint.Models;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.PlatformUserClaims;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

using Sorcha.Blueprint.Models.Canonical;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Guards issue #1284 (H-REHEARSAL) at the ORCHESTRATION seam. Feature 142's Full Rehearsal could
/// never pass for any blueprint whose action declares an <c>x-claim-source</c> binding — which
/// includes the AIAS assured-identity template used for the conference demo — because
/// <see cref="RehearsalOrchestrationService.SubmitStepAsync"/> hardcoded <c>caller: null</c> when
/// calling the real <see cref="IActionExecutionService"/>, and <c>ActionExecutionService</c> throws
/// when an x-claim-source action gets a null caller (it has nobody to resolve live values for).
/// <para>
/// <see cref="RehearsalOrchestrationServiceTests"/> mocks <c>IActionExecutionService</c> wholesale —
/// exactly why this went uncaught, since the mock succeeds no matter what <c>caller</c> is. This
/// suite instead wires the REAL <c>ActionExecutionService</c> into the rehearsal pipeline, mirroring
/// the harness in <c>ActionExecutionLiveClaimSourceTests</c> (guards the sibling #1264 defect).
/// </para>
/// <para>
/// SCOPE NOTE: this suite deliberately does NOT assert <c>RehearsalOutcome.Passed</c>, because
/// reaching a terminal state is no longer decided at this seam. Feature 145 changed
/// <c>ActionExecutionService.ExecuteAsync</c> to an always-async "submit and let the
/// InstanceProjector fold the sealed docket" contract — every successful call returns
/// <c>IsComplete=false</c> / <c>NextActions=[]</c> unconditionally (all three
/// <c>return new ActionSubmissionResponse</c> sites agree). <c>RehearsalOrchestrationService</c>
/// therefore reads the outcome from the instance projection instead; that reconciliation is a
/// separate, independently-guarded concern — see
/// <c>RehearsalOrchestrationServiceTests.SubmitStep_TerminalComesFromTheProjection_NotTheResponse</c>
/// and the README section "Full Rehearsal reads its outcome from the ledger, not the response".
/// This suite proves the concrete, in-scope claim: the caller:null defect is gone (the step no longer
/// fails) and the LIVE server value — not the client's submission — is what gets validated.
/// </para>
/// </summary>
public class RehearsalClaimSourceOrchestrationTests
{
    private const string OrgId = "org-claim";
    private const string BlueprintId = "bp-claim";
    private const string SandboxRegisterId = "sandbox-reg-claim";
    private const string SandboxWalletAddress = "ws11q-sandbox-citizen";
    private static readonly Guid RehearsalInitiatorId = Guid.Parse("22222222-3333-4444-5555-666666666666");

    // ---- Orchestration-level collaborators ----
    private readonly Mock<ISandboxRegisterProvider> _sandboxProvider = new();
    private readonly Mock<IBlueprintStore> _blueprintStore = new();
    private readonly Mock<IPublishService> _publishService = new();
    private readonly InMemoryRehearsalPassStore _passStore = new();

    // Shared: orchestration mints wallets with this client; real execution signs with it too.
    private readonly Mock<IWalletServiceClient> _walletClient = new();
    // Shared: orchestration creates the sandbox instance; real execution reads/updates it.
    private readonly Mock<IInstanceStore> _instanceStore = new();

    // ---- Real ActionExecutionService collaborators (mirrors ActionExecutionLiveClaimSourceTests) ----
    private readonly Mock<IActionResolverService> _actionResolver = new();
    private readonly Mock<IStateReconstructionService> _stateReconstruction = new();
    private readonly Mock<ITransactionBuilderService> _transactionBuilder = new();
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly Mock<IValidatorServiceClient> _validatorClient = new();
    private readonly Mock<IParticipantServiceClient> _participantClient = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IActionStore> _actionStore = new();
    private readonly Mock<IExecutionEngine> _executionEngine = new();
    private readonly Mock<IPlatformUserClaimsClient> _platformUserClaims = new();

    private Instance? _storedInstance;

    /// <summary>The payload as it reached validation — i.e. what would be signed and sealed.</summary>
    private Dictionary<string, object>? _validatedPayload;

    public RehearsalClaimSourceOrchestrationTests()
    {
        // ---- Orchestration-level wiring ----
        _sandboxProvider
            .Setup(p => p.GetOrCreateSandboxRegisterAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SandboxRegisterId);

        _walletClient
            .Setup(w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new WalletInfo
            {
                Address = SandboxWalletAddress,
                Name = "sandbox",
                PublicKey = "pk",
                Algorithm = "ED25519",
                Status = "Active",
                Tenant = OrgId,
                Owner = "sandbox-owner",
            });

        _publishService
            .Setup(p => p.ValidateAsync(BlueprintId))
            .ReturnsAsync(new BlueprintValidationResult(BlueprintId, "Title", true, [], []));
        // Feature 195 — a real publish assigns the definition's identity, and the rehearsal instance
        // is pinned to it. A stand-in returning no PublicationTxId models a publish that cannot
        // happen, and the rehearsal then fails exactly as a real unpinned instance would.
        _publishService
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string id, string reg) =>
                PublishResult.Success(new PublishedBlueprint
                {
                    BlueprintId = id,
                    RegisterId = reg,
                    PublicationTxId = BlueprintPublicationId.Compute(reg, id, "{}")
                }));

        _blueprintStore
            .Setup(s => s.GetAsync(BlueprintId))
            .ReturnsAsync(ClaimSourceBlueprint());
        _blueprintStore
            .Setup(s => s.AddAsync(It.IsAny<BlueprintModel>()))
            .ReturnsAsync((BlueprintModel bp) => bp);

        _instanceStore
            .Setup(s => s.CreateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance i, CancellationToken _) => { _storedInstance = i; return i; });
        _instanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CancellationToken _) => _storedInstance);
        _instanceStore
            .Setup(s => s.UpdateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance i, CancellationToken _) => { _storedInstance = i; return i; });

        // ---- Real ActionExecutionService wiring ----
        var blueprint = ClaimSourceBlueprint();
        var action = blueprint.Actions!.First();

        _actionResolver.Setup(x => x.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _actionResolver.Setup(x => x.GetActionDefinition(It.IsAny<BlueprintModel>(), "1"))
            .Returns(action);
        _actionStore.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

        _stateReconstruction.Setup(x => x.ReconstructAsync(
                It.IsAny<BlueprintModel>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState());

        // Captures the payload exactly as it reaches validation — injection runs before this.
        _executionEngine.Setup(x => x.ValidateAsync(
                It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .Callback<Dictionary<string, object>, ActionModel, CancellationToken>(
                (data, _, _) => _validatedPayload = new Dictionary<string, object>(data))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());
        _executionEngine.Setup(x => x.ApplyCalculationsAsync(
                It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());
        _executionEngine.Setup(x => x.DetermineRoutingWithMappingAsync(
                blueprint, action, It.IsAny<Dictionary<string, object>>(),
                It.IsAny<System.Text.Json.Nodes.JsonObject?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());
        _executionEngine.Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());

        _registerClient.Setup(x => x.GetRegisterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = SandboxRegisterId, Name = "Sandbox", DevMode = true });
        _registerClient.Setup(x => x.ResolvePublicKeysBatchAsync(
                It.IsAny<string>(), It.IsAny<BatchPublicKeyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse { Resolved = new(), NotFound = [], Revoked = [] });
        _registerClient.Setup(x => x.GetTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel { TxId = new string('a', 64), DocketNumber = 1 });

        _walletClient.Setup(x => x.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64], PublicKey = new byte[32],
                SignedBy = SandboxWalletAddress, Algorithm = "ED25519",
            });
        _validatorClient.Setup(x => x.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = new string('a', 64) });
    }

    private RehearsalOrchestrationService CreateOrchestrationServiceWithRealExecution()
    {
        var realExecution = new ActionExecutionService(
            _actionResolver.Object, _stateReconstruction.Object, _transactionBuilder.Object,
            _registerClient.Object, _validatorClient.Object, _walletClient.Object,
            _participantClient.Object, _notificationService.Object, _instanceStore.Object,
            _actionStore.Object, _executionEngine.Object,
            NullLogger<ActionExecutionService>.Instance, new ConfigurationBuilder().Build(),
            jsonLogicEvaluator: new JsonLogicEvaluator(),
            platformUserClaims: _platformUserClaims.Object);

        var services = new ServiceCollection();
        services.AddScoped(_ => _blueprintStore.Object);
        services.AddScoped(_ => _publishService.Object);
        services.AddScoped(_ => _walletClient.Object);
        services.AddScoped<IActionExecutionService>(_ => realExecution);
        services.AddScoped(_ => _instanceStore.Object);
        services.AddSingleton<IRehearsalPassStore>(_passStore);
        services.AddMetrics();
        var provider = services.BuildServiceProvider();
        var metrics = new BlueprintDesignerMetrics(
            provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());

        return new RehearsalOrchestrationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _sandboxProvider.Object,
            metrics,
            NullLogger<RehearsalOrchestrationService>.Instance,
            new ExecutableDefinitionHasher(),
            // This harness drives the REAL ActionExecutionService but runs no InstanceProjector, so
            // nothing ever folds the submitted transaction. Post-F145 the step therefore always ends
            // at the sealing-timeout — keep the wait in milliseconds rather than the 90s production
            // default, and assert on WHICH end state it reaches (below).
            projectionTimeout: TimeSpan.FromMilliseconds(150),
            projectionPollInterval: TimeSpan.FromMilliseconds(10));
    }

    /// <summary>The AIAS action-1 shape: a headless, read-only emailVerified field bound to the claim.</summary>
    private static BlueprintModel ClaimSourceBlueprint() => new()
    {
        Id = BlueprintId,
        Title = "AIAS-shaped claim-source rehearsal fixture",
        OrganizationId = OrgId,
        // No WalletAddress — the starting participant is OPEN (Feature 103 late-binding), matching
        // what BuildSandboxBlueprint actually produces for a starting-action sender.
        Participants = [new ParticipantModel { Id = "citizen", Name = "Applicant" }],
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

    [Fact]
    public async Task SubmitStep_ActionWithClaimSourceBinding_ResolvesLiveValueForRehearsalInitiator_DoesNotFail()
    {
        // The platform's live state: the rehearsal's OWN initiator has verified their email.
        _platformUserClaims
            .Setup(x => x.ResolveAsync(
                RehearsalInitiatorId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["email_verified"] = "true" });

        var service = CreateOrchestrationServiceWithRealExecution();
        var started = await service.StartFullAsync(BlueprintId, OrgId, RehearsalInitiatorId);
        await service.SwitchRoleAsync(started.RehearsalId, "citizen");

        // The client submits the STALE/false value on purpose — the platform must overwrite it,
        // exactly as it would for a live submission (#1264).
        var result = await service.SubmitStepAsync(
            started.RehearsalId, actionId: 1, payloadJson: """{"name":"Stuart","emailVerified":false}""");

        result.Should().NotBeNull();

        // #1284: the claim-source path must no longer throw. That is asserted by its ABSENCE from the
        // log plus the fact that execution reached schema validation (below) — NOT by the overall
        // outcome, because this harness runs no InstanceProjector, so post-F145 the step legitimately
        // ends at the named sealing-timeout. Asserting "not Failed" here would be asserting that a
        // projector we never started did its job.
        result!.Log.Should().NotContain(e => e.Message.Contains("no usable"),
            "the caller:null symptom (#1284) must not resurface in the rehearsal log");
        result.Log.Should().Contain(e => e.Message.Contains("did not seal"),
            "with no projector in this harness the step must end at the named sealing-timeout — "
            + "which is the correct end state, and distinguishable from a claim-source failure");

        _validatedPayload.Should().NotBeNull(
            "execution must reach schema validation rather than throwing before it");
        _validatedPayload!["emailVerified"].Should().Be(true,
            "the LIVE value for the rehearsal's own initiator (StartedByPlatformUserId) must be what " +
            "gets validated — not the client's stale/false submission");

        _platformUserClaims.Verify(x => x.ResolveAsync(
            RehearsalInitiatorId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "claim resolution must be scoped to the rehearsal's own initiator, not an arbitrary user");
    }
}
