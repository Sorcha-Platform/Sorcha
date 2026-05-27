// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 142 (T034 / T037 + T038 / FR-027 + FR-032) — tests for <see cref="PublishGate"/>:
/// the governance hard gate (refuse callers without a publish-governance role on the register)
/// and the rehearsal soft gate (block un-rehearsed exec-defs unless an authorised caller
/// confirms an override). Verifies that a refused caller produces no proceed outcome and that
/// the override path returns a clear "proceed with override" decision carrying the exec-def hash.
/// </summary>
public class PublishGateTests
{
    private const string BlueprintId = "bp-1";
    private const string RegisterId = "reg-live-1";
    private const string CallerWallet = "ws11q-caller-wallet";
    private const string CallerOrg = "org-1";

    private readonly Mock<IBlueprintStore> _blueprintStore = new();
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly InMemoryRehearsalPassStore _passStore = new();
    private readonly ExecutableDefinitionHasher _hasher = new();

    public PublishGateTests()
    {
        _blueprintStore.Setup(s => s.GetAsync(BlueprintId)).ReturnsAsync(TwoStepBlueprint());
    }

    private PublishGate CreateGate() => new(
        _blueprintStore.Object,
        _registerClient.Object,
        _passStore,
        NullLogger<PublishGate>.Instance,
        _hasher);

    private static PublishCaller AuthorisedCaller() => new(
        PlatformUserId: Guid.NewGuid(),
        OrganizationId: CallerOrg,
        WalletAddress: CallerWallet);

    private void SetRoster(params RosterMember[] members) =>
        _registerClient
            .Setup(c => c.GetGovernanceRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceRosterResponse
            {
                RegisterId = RegisterId,
                Members = members.ToList(),
                MemberCount = members.Length,
            });

    private static RosterMember Member(string subject, string role) => new()
    {
        Subject = subject,
        Role = role,
        Algorithm = "ED25519",
        GrantedAt = DateTimeOffset.UtcNow,
    };

    private string ExpectedHash() => _hasher.ComputeHash(TwoStepBlueprint());

    private async Task SeedPassAsync()
    {
        await _passStore.RecordAsync(new RehearsalPass
        {
            BlueprintId = BlueprintId,
            ExecDefHash = ExpectedHash(),
            RehearsedAt = DateTimeOffset.UtcNow,
            RehearsedByPlatformUserId = Guid.NewGuid(),
            SandboxRegisterId = "sandbox-reg",
        });
    }

    // -------------------------------------------------------------------------
    // 1) Governance HARD gate (FR-027/D5)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Evaluate_CallerNotInRoster_ReturnsForbidden()
    {
        // Roster holds a publishing role, but it belongs to someone else.
        SetRoster(Member("did:sorcha:w:someone-else", "Owner"));
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: false);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden);
        decision.Reason.Should().NotBeNullOrEmpty();
        decision.ExecDefHash.Should().Be(ExpectedHash());
    }

    [Fact]
    public async Task Evaluate_CallerHasNonPublishingRole_ReturnsForbidden()
    {
        // Caller is in the roster, but only as an Auditor (not a publishing role).
        SetRoster(Member($"did:sorcha:w:{CallerWallet}", "Auditor"));
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: false);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden);
    }

    [Fact]
    public async Task Evaluate_EmptyRoster_FailsClosedForbidden()
    {
        SetRoster(); // no members at all
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: false);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden);
    }

    [Fact]
    public async Task Evaluate_NullRoster_FailsClosedForbidden()
    {
        _registerClient
            .Setup(c => c.GetGovernanceRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GovernanceRosterResponse?)null);
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: false);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden);
    }

    [Fact]
    public async Task Evaluate_ForbiddenCaller_DoesNotConsultRehearsalPass()
    {
        // Even with a matching pass present, an unauthorised caller is refused before the soft gate.
        await SeedPassAsync();
        SetRoster(Member("did:sorcha:w:someone-else", "Owner"));
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden);
    }

    // -------------------------------------------------------------------------
    // 2) Rehearsal SOFT gate (FR-032/D4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Evaluate_AuthorisedAndPassMatches_ReturnsProceed()
    {
        await SeedPassAsync();
        SetRoster(Member($"did:sorcha:w:{CallerWallet}", "Designer"));
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: false);

        decision.Outcome.Should().Be(PublishGateOutcome.Proceed);
        decision.ExecDefHash.Should().Be(ExpectedHash());
    }

    [Fact]
    public async Task Evaluate_AuthorisedNoPassNoOverride_ReturnsRehearsalRequired()
    {
        // No rehearsal pass seeded.
        SetRoster(Member($"did:sorcha:w:{CallerWallet}", "Admin"));
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: false);

        decision.Outcome.Should().Be(PublishGateOutcome.RehearsalRequired);
        decision.ExecDefHash.Should().Be(ExpectedHash());
    }

    [Fact]
    public async Task Evaluate_AuthorisedNoPassWithOverride_ReturnsProceedWithOverride()
    {
        SetRoster(Member($"did:sorcha:w:{CallerWallet}", "Owner"));
        var gate = CreateGate();

        var decision = await gate.EvaluateAsync(AuthorisedCaller(), BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.ProceedWithOverride);
        decision.ExecDefHash.Should().Be(ExpectedHash());
    }

    [Fact]
    public async Task Evaluate_MatchesByOrgIdWhenNoWallet_ReturnsProceedPath()
    {
        // Roster subject embeds the org id; caller has no wallet claim.
        await SeedPassAsync();
        SetRoster(Member($"did:sorcha:org:{CallerOrg}", "Owner"));
        var gate = CreateGate();
        var caller = new PublishCaller(Guid.NewGuid(), CallerOrg, WalletAddress: null);

        var decision = await gate.EvaluateAsync(caller, BlueprintId, RegisterId, overrideConfirmed: false);

        decision.Outcome.Should().Be(PublishGateOutcome.Proceed);
    }

    [Fact]
    public async Task Evaluate_UnknownBlueprint_Throws()
    {
        _blueprintStore.Setup(s => s.GetAsync("missing")).ReturnsAsync((BlueprintModel?)null);
        var gate = CreateGate();

        var act = () => gate.EvaluateAsync(AuthorisedCaller(), "missing", RegisterId, overrideConfirmed: false);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static BlueprintModel TwoStepBlueprint() => new()
    {
        Id = BlueprintId,
        Title = "Two Step",
        OrganizationId = CallerOrg,
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
}
