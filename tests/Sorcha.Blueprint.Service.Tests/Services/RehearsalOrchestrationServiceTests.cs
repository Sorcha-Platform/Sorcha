// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Blueprint.Models;
using Sorcha.ServiceClients.Wallet;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 142 (T028 / T022) — tests for <see cref="RehearsalOrchestrationService"/>: sandbox
/// provisioning + ephemeral wallet minting on start, RehearsalPass on terminal success, reset
/// idempotency, and sandbox isolation (the orchestration only ever targets the sandbox register).
/// </summary>
public class RehearsalOrchestrationServiceTests
{
    private const string OrgId = "org-1";
    private const string BlueprintId = "bp-1";
    private const string SandboxRegisterId = "sandbox-reg-abc";
    private const string LiveRegisterId = "live-reg-DANGER";

    private readonly Mock<ISandboxRegisterProvider> _sandboxProvider = new();
    private readonly Mock<IWalletServiceClient> _walletClient = new();
    private readonly Mock<IBlueprintStore> _blueprintStore = new();
    private readonly Mock<IPublishService> _publishService = new();
    private readonly Mock<IActionExecutionService> _execution = new();
    private readonly InMemoryRehearsalPassStore _passStore = new();
    private readonly Mock<IInstanceStore> _instanceStore = new();

    private int _walletCounter;

    public RehearsalOrchestrationServiceTests()
    {
        _sandboxProvider
            .Setup(p => p.GetOrCreateSandboxRegisterAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SandboxRegisterId);

        _walletClient
            .Setup(w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new WalletInfo
            {
                Address = $"ws11q-sandbox-{Interlocked.Increment(ref _walletCounter)}",
                Name = "sandbox",
                PublicKey = "pk",
                Algorithm = "ED25519",
                Status = "Active",
                Tenant = OrgId,
                Owner = "sandbox-owner",
            });

        // Default: blueprint valid, publish succeeds, instance creates.
        _publishService
            .Setup(p => p.ValidateAsync(BlueprintId))
            .ReturnsAsync(new BlueprintValidationResult(BlueprintId, "Title", true, [], []));

        // Matches both the original BlueprintId and the sandbox clone id created by
        // BuildSandboxBlueprint → AddAsync (see ctor mock above for AddAsync).
        _publishService
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string id, string reg) =>
                PublishResult.Success(new PublishedBlueprint { BlueprintId = id, RegisterId = reg }));

        _instanceStore
            .Setup(s => s.CreateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance i, CancellationToken _) => i);

        _blueprintStore
            .Setup(s => s.GetAsync(BlueprintId))
            .ReturnsAsync(TwoStepBlueprint());

        // Sandbox-clone publish path: StartFullAsync calls AddAsync on a sandbox-specific
        // COPY of the blueprint (line 142 of RehearsalOrchestrationService). The default
        // Moq return is null which would NRE on `savedSandboxBlueprint.Id`; return the
        // input with a deterministic sandbox id so downstream assertions can rely on it.
        _blueprintStore
            .Setup(s => s.AddAsync(It.IsAny<BlueprintModel>()))
            .ReturnsAsync((BlueprintModel bp) =>
            {
                bp.Id = string.IsNullOrEmpty(bp.Id) ? $"{BlueprintId}-sandbox" : bp.Id;
                return bp;
            });
    }

    private RehearsalOrchestrationService CreateService()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _blueprintStore.Object);
        services.AddScoped(_ => _publishService.Object);
        services.AddScoped(_ => _walletClient.Object);
        services.AddScoped(_ => _execution.Object);
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
            // Short projection wait so the timeout path is testable in milliseconds rather than the
            // 90s production default.
            projectionTimeout: TimeSpan.FromMilliseconds(150),
            projectionPollInterval: TimeSpan.FromMilliseconds(10));
    }

    // -------------------------------------------------------------------------
    // StartFull
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartFull_ProvisionsSandbox_MintsPerRoleWallets_ReturnsInProgressWithSteps()
    {
        var service = CreateService();

        var rehearsal = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());

        rehearsal.Outcome.Should().Be(RehearsalOutcome.InProgress);
        rehearsal.Mode.Should().Be(RehearsalMode.Full);
        rehearsal.SandboxRegisterId.Should().Be(SandboxRegisterId);
        rehearsal.Steps.Should().HaveCount(2);
        rehearsal.Steps[0].Status.Should().Be(RehearsalStepStatus.Current);
        rehearsal.Steps[1].Status.Should().Be(RehearsalStepStatus.Pending);

        // One ephemeral wallet per participant role (2 participants).
        _walletClient.Verify(w => w.CreateWalletAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            OrgId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _sandboxProvider.Verify(p => p.GetOrCreateSandboxRegisterAsync(
            OrgId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartFull_BlueprintHasBlockingErrors_ThrowsRehearsalValidationException()
    {
        _publishService
            .Setup(p => p.ValidateAsync(BlueprintId))
            .ReturnsAsync(new BlueprintValidationResult(
                BlueprintId, "Title", false,
                [new ValidationIssueDto("error", "Action 1 sender is not a participant")],
                []));
        var service = CreateService();

        var act = () => service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());

        var ex = (await act.Should().ThrowAsync<RehearsalValidationException>()).Which;
        ex.Errors.Should().ContainSingle().Which.Should().Contain("not a participant");

        // No sandbox provisioned, no wallets minted when validation blocks.
        _sandboxProvider.Verify(p => p.GetOrCreateSandboxRegisterAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _walletClient.Verify(w => w.CreateWalletAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Sandbox isolation — only ever targets the sandbox register
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitStep_OnlyEverTargetsSandboxRegister_NeverLive()
    {
        _execution
            .Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NextActionResponseFor(nextActionId: 2));

        // The projector folds tx-step1 and routes to action 2 — without this the step would take the
        // (correct) projection-timeout path instead of the success path under test.
        _instanceStore
            .Setup(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Projected("tx-step1", 2));

        var platformUserId = Guid.NewGuid();
        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, platformUserId);
        await service.SwitchRoleAsync(started.RehearsalId, "applicant");

        await service.SubmitStepAsync(started.RehearsalId, actionId: 1, payloadJson: "{\"x\":1}");

        // Assert the execution pipeline + publish only ever saw the sandbox register id.
        // Issue #1284 — caller is no longer null: it is a synthetic principal carrying the
        // rehearsal's own initiator (StartedByPlatformUserId), so x-claim-source bindings resolve
        // that person's live values instead of ActionExecutionService always throwing.
        _execution.Verify(e => e.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.Is<ActionSubmissionRequest>(r => r.RegisterAddress == SandboxRegisterId),
            It.IsAny<string>(), CallerFor(platformUserId), It.IsAny<CancellationToken>()), Times.Once);
        _execution.Verify(e => e.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.Is<ActionSubmissionRequest>(r => r.RegisterAddress == LiveRegisterId),
            It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        // The sandbox-clone fix (commits 5fd21785/180dd54e) publishes a CLONE of the blueprint
        // with ephemeral wallets baked into non-starting participants; the clone gets its own
        // id inside BuildSandboxBlueprint. The invariant under test is the register target —
        // sandbox always, never live — so we match any blueprint id and assert the register.
        _publishService.Verify(p => p.PublishAsync(It.IsAny<string>(), SandboxRegisterId), Times.Once);
        _publishService.Verify(p => p.PublishAsync(It.IsAny<string>(), LiveRegisterId), Times.Never);
        _instanceStore.Verify(s => s.CreateAsync(
            It.Is<Instance>(i => i.RegisterId == SandboxRegisterId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitStep_SignsAsActingRoleWallet()
    {
        _execution
            .Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NextActionResponseFor(nextActionId: 2));

        // The projector folds tx-step1 and routes to action 2 — without this the step would take the
        // (correct) projection-timeout path instead of the success path under test.
        _instanceStore
            .Setup(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Projected("tx-step1", 2));

        var platformUserId = Guid.NewGuid();
        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, platformUserId);
        await service.SwitchRoleAsync(started.RehearsalId, "applicant");

        await service.SubmitStepAsync(started.RehearsalId, 1, "{}");

        // The acting role's ephemeral sandbox wallet is the SenderWallet (server signs as it).
        // Issue #1284 — caller carries the rehearsal's own initiator, not null (see above).
        _execution.Verify(e => e.ExecuteAsync(
            It.IsAny<string>(), 1,
            It.Is<ActionSubmissionRequest>(r => r.SenderWallet.StartsWith("ws11q-sandbox-")),
            It.IsAny<string>(), CallerFor(platformUserId), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Matches the synthetic rehearsal caller principal built in
    /// <see cref="RehearsalOrchestrationService.SubmitStepAsync"/> — a non-null principal carrying
    /// <c>platform_user_id</c> equal to the rehearsal's own initiator (Issue #1284). Verifying the
    /// exact claim value (not just non-null) is what would have caught the original defect being
    /// hardcoded <c>null</c>.
    /// </summary>
    private static System.Security.Claims.ClaimsPrincipal? CallerFor(Guid platformUserId) =>
        It.Is<System.Security.Claims.ClaimsPrincipal?>(p =>
            p != null && p.FindFirst(TokenClaimConstants.PlatformUserId)!.Value == platformUserId.ToString());

    // -------------------------------------------------------------------------
    // RehearsalPass on terminal success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitStep_TerminalSuccess_WritesExactlyOneRehearsalPassWithCurrentExecDefHash()
    {
        // First step routes to action 2; second step completes the workflow.
        _execution
            .SetupSequence(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NextActionResponseFor(nextActionId: 2))
            .ReturnsAsync(CompleteResponse());

        // The projector folds each submission in turn. Step 1 leaves action 2 current; the final step
        // leaves none, which is what InstanceProjection turns into State = Completed.
        _instanceStore
            .SetupSequence(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Projected("tx-step1", 2))
            .ReturnsAsync(Projected("tx-final"));

        var hasher = new ExecutableDefinitionHasher();
        var expectedHash = hasher.ComputeHash(TwoStepBlueprint());

        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());

        await service.SwitchRoleAsync(started.RehearsalId, "applicant");
        await service.SubmitStepAsync(started.RehearsalId, 1, "{}");
        await service.SwitchRoleAsync(started.RehearsalId, "approver");
        var final = await service.SubmitStepAsync(started.RehearsalId, 2, "{}");

        final!.Outcome.Should().Be(RehearsalOutcome.Passed);

        var pass = await _passStore.GetLatestAsync(BlueprintId, expectedHash);
        pass.Should().NotBeNull();
        pass!.ExecDefHash.Should().Be(expectedHash);
        pass.ExecDefHash.Should().Be(started.ExecDefHash);
        pass.SandboxRegisterId.Should().Be(SandboxRegisterId);
    }

    [Fact]
    public async Task SubmitStep_NonTerminal_DoesNotWriteRehearsalPass()
    {
        _execution
            .Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NextActionResponseFor(nextActionId: 2));

        // The projector folds tx-step1 and routes to action 2 — without this the step would take the
        // (correct) projection-timeout path instead of the success path under test.
        _instanceStore
            .Setup(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Projected("tx-step1", 2));

        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());
        await service.SwitchRoleAsync(started.RehearsalId, "applicant");

        var after = await service.SubmitStepAsync(started.RehearsalId, 1, "{}");

        after!.Outcome.Should().Be(RehearsalOutcome.InProgress);
        var pass = await _passStore.GetLatestAsync(BlueprintId, started.ExecDefHash);
        pass.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Reset — discards instance + wallet map, idempotent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Reset_DiscardsInstanceAndWalletMap_SecondResetIsIdempotent()
    {
        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());

        var first = await service.ResetAsync(started.RehearsalId);
        var second = await service.ResetAsync(started.RehearsalId);

        first.Should().BeTrue();
        second.Should().BeTrue(); // session still present; reset is idempotent

        var afterReset = await service.GetAsync(started.RehearsalId);
        afterReset!.Outcome.Should().Be(RehearsalOutcome.Abandoned);
        afterReset.CurrentActingRole.Should().BeEmpty();
        afterReset.Steps.Should().OnlyContain(s => s.Status == RehearsalStepStatus.Pending);
    }

    [Fact]
    public async Task Reset_UnknownRehearsal_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.ResetAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitStep_AfterReset_Throws()
    {
        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());
        await service.ResetAsync(started.RehearsalId);

        var act = () => service.SubmitStepAsync(started.RehearsalId, 1, "{}");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // Get / SwitchRole
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_UnknownRehearsal_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task SwitchRole_UnknownRole_Throws()
    {
        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());

        var act = () => service.SwitchRoleAsync(started.RehearsalId, "nope");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static BlueprintModel TwoStepBlueprint() => new()
    {
        Id = BlueprintId,
        Title = "Two Step",
        OrganizationId = OrgId,
        Participants =
        [
            new ParticipantModel { Id = "applicant", Name = "Applicant" },
            new ParticipantModel { Id = "approver", Name = "Approver" },
        ],
        Actions =
        [
            new ActionModel { Id = 1, Title = "Apply", Sender = "applicant", IsStartingAction = true },
            new ActionModel { Id = 2, Title = "Approve", Sender = "approver" },
        ],
    };

    // ── F145 reconciliation: outcome + routing come from the PROJECTION ─────────────────────

    [Fact]
    public async Task SubmitStep_TerminalComesFromTheProjection_NotTheResponse()
    {
        // The response says IsComplete=false (it always does). The projection says the workflow
        // completed. The projection is the ledger's answer and must win — otherwise the F142 gate can
        // never be earned by any blueprint.
        _execution
            .Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedResponse("tx-only"));

        _instanceStore
            .Setup(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Projected("tx-only"));   // no current actions => Completed

        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());
        await service.SwitchRoleAsync(started.RehearsalId, "applicant");

        var result = await service.SubmitStepAsync(started.RehearsalId, 1, "{}");

        result!.Outcome.Should().Be(RehearsalOutcome.Passed,
            "the projection reported the workflow complete, even though the response could not");

        var pass = await _passStore.GetLatestAsync(BlueprintId, started.ExecDefHash);
        pass.Should().NotBeNull("a passed rehearsal must record the RehearsalPass that unlocks Go live");
    }

    [Fact]
    public async Task SubmitStep_NextStepComesFromTheProjection_NotTheAlwaysEmptyResponse()
    {
        // response.NextActions is hard-coded [] post-F145, so routing must be read from the
        // projection's CurrentActionIds or the walk-through can never leave step 1.
        _execution
            .Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedResponse("tx-step1"));

        _instanceStore
            .Setup(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Projected("tx-step1", 2));   // action 2 now current

        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());
        await service.SwitchRoleAsync(started.RehearsalId, "applicant");

        var result = await service.SubmitStepAsync(started.RehearsalId, 1, "{}");

        result!.Outcome.Should().Be(RehearsalOutcome.InProgress);
        result.Steps.Single(s => s.ActionId == 2).Status.Should().Be(RehearsalStepStatus.Current,
            "the projection routed to action 2, so the walk-through must advance to it");
        result.Steps.Single(s => s.ActionId == 1).Status.Should().Be(RehearsalStepStatus.Done);
    }

    [Fact]
    public async Task SubmitStep_ProjectionNeverArrives_FailsWithANamedReason_AndRecordsNoPass()
    {
        // The submission was accepted but the sandbox register did not seal it in time. That is a
        // sealing delay, NOT a broken blueprint, and the message must say so — while still refusing
        // to record a pass it cannot vouch for.
        _execution
            .Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedResponse("tx-never-sealed"));

        ProjectionNeverFolds();

        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());
        await service.SwitchRoleAsync(started.RehearsalId, "applicant");

        var result = await service.SubmitStepAsync(started.RehearsalId, 1, "{}");

        result!.Outcome.Should().Be(RehearsalOutcome.Failed);
        result.Log.Should().Contain(e =>
            e.Kind == RehearsalEventKind.Error && e.Message.Contains("did not seal"),
            "the failure must name the sealing timeout, not read as a generic error");
        result.Log.Should().Contain(e => e.Message.Contains("not a blueprint problem"),
            "the designer must be able to tell a slow node from a broken blueprint");

        var pass = await _passStore.GetLatestAsync(BlueprintId, started.ExecDefHash);
        pass.Should().BeNull("an unverified pass would be worse than no pass");
    }

    [Fact]
    public async Task SubmitStep_ProjectionReportsRejection_FailsNamed_AndRecordsNoPass()
    {
        _execution
            .Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ActionSubmissionRequest>(),
                It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedResponse("tx-rejected"));

        ProjectionRejected("tx-rejected");

        var service = CreateService();
        var started = await service.StartFullAsync(BlueprintId, OrgId, Guid.NewGuid());
        await service.SwitchRoleAsync(started.RehearsalId, "applicant");

        var result = await service.SubmitStepAsync(started.RehearsalId, 1, "{}");

        result!.Outcome.Should().Be(RehearsalOutcome.Failed);
        result.Log.Should().Contain(e =>
            e.Kind == RehearsalEventKind.Error && e.Message.Contains("rejected"));
        (await _passStore.GetLatestAsync(BlueprintId, started.ExecDefHash)).Should().BeNull();
    }

    // ── F145-faithful fixtures ──────────────────────────────────────────────────────────────
    // Post-Feature-145 ExecuteAsync ALWAYS returns 202 with IsComplete=false and NextActions=[]:
    // routing goes onto the on-chain RoutingDecision for the InstanceProjector, never onto the
    // response. The previous fixtures modelled a synchronous response (`IsComplete = true`,
    // populated NextActions) that production is structurally incapable of returning — so these
    // tests passed while the feature could not work at all. They now model what really comes back,
    // and the outcome is driven by a projected instance instead.

    /// <summary>What ExecuteAsync actually returns for every accepted submission (202, async).</summary>
    private static ActionSubmissionResponse AcceptedResponse(string txId) => new()
    {
        TransactionId = txId,
        InstanceId = "inst-1",
        IsAsync = true,
        IsComplete = false,
        NextActions = [],
    };

    /// <summary>
    /// Builds the instance projection as the InstanceProjector would leave it once
    /// <paramref name="foldedTxId"/> is folded: <c>LastAppliedTxId</c> set, and state derived from
    /// whether any current actions remain — mirroring <c>InstanceProjection.ApplyInPlace</c>.
    /// </summary>
    private static Instance Projected(string? foldedTxId, params int[] currentActionIds) => new()
    {
        Id = "inst-1",
        BlueprintId = BlueprintId,
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        TenantId = OrgId,
        RegisterId = SandboxRegisterId,
        LastAppliedTxId = foldedTxId,
        LastTransactionId = foldedTxId,
        CurrentActionIds = [.. currentActionIds],
        State = currentActionIds.Length == 0 ? InstanceState.Completed : InstanceState.Active,
    };

    /// <summary>Stubs a projection that never folds the submitted tx — the timeout path.</summary>
    private void ProjectionNeverFolds() =>
        _instanceStore
            .Setup(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Projected(foldedTxId: null, 1));

    /// <summary>Stubs a projection where the fold recorded a terminal REJECTION.</summary>
    private void ProjectionRejected(string txId) =>
        _instanceStore
            .Setup(st => st.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Instance
            {
                Id = "inst-1",
                BlueprintId = BlueprintId,
                BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
                BlueprintVersion = 1,
                TenantId = OrgId,
                RegisterId = SandboxRegisterId,
                LastAppliedTxId = txId,
                CurrentActionIds = [],
                State = InstanceState.Rejected,
            });

    private static ActionSubmissionResponse NextActionResponseFor(int nextActionId, string txId = "tx-step1") => new()
    {
        TransactionId = txId,
        InstanceId = "inst-1",
        IsComplete = false,
        NextActions =
        [
            new NextActionResponse
            {
                ActionId = nextActionId,
                ActionTitle = "Approve",
                ParticipantId = "approver",
            },
        ],
    };

    /// <summary>
    /// The FINAL step's response. Note it is identical in shape to any other accepted submission —
    /// completion is NOT expressible on the response post-F145; it is observed on the projection.
    /// </summary>
    private static ActionSubmissionResponse CompleteResponse() => AcceptedResponse("tx-final");
}
