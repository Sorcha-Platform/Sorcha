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
            new ExecutableDefinitionHasher());
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

    private static ActionSubmissionResponse NextActionResponseFor(int nextActionId) => new()
    {
        TransactionId = "tx-" + Guid.NewGuid().ToString("N")[..8],
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

    private static ActionSubmissionResponse CompleteResponse() => new()
    {
        TransactionId = "tx-final",
        InstanceId = "inst-1",
        IsComplete = true,
        NextActions = [],
    };
}
