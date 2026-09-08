// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.OrgInfo;
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

    private const string OrgWallet = "ws11q-organisation-signing-wallet";

    private readonly Mock<IBlueprintStore> _blueprintStore = new();
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly Mock<IOrgInfoClient> _orgInfoClient = new();
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
        _orgInfoClient.Object,
        _hasher);

    private static PublishCaller AuthorisedCaller() => new(
        PlatformUserId: Guid.NewGuid(),
        OrganizationId: CallerOrg,
        WalletAddress: CallerWallet);

    /// <summary>
    /// The #1620 shape: an org admin with NO linked wallet of their own. This is the normal case
    /// after #1525 — the org owns its signing wallet, so no user token carries that address.
    /// </summary>
    private static PublishCaller OrgAdminWithoutOwnWallet() => new(
        PlatformUserId: Guid.NewGuid(),
        OrganizationId: OrgGuid.ToString(),
        WalletAddress: null,
        HoldsOrgPublishRole: true);

    /// <summary>The same person without an Administrator/Designer role.</summary>
    private static PublishCaller OrdinaryOrgMember() => new(
        PlatformUserId: Guid.NewGuid(),
        OrganizationId: OrgGuid.ToString(),
        WalletAddress: null,
        HoldsOrgPublishRole: false);

    private static readonly Guid OrgGuid = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private void OrgResolvesToItsWallet() =>
        _orgInfoClient
            .Setup(c => c.ResolveCanonicalWalletAddressAsync(OrgGuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrgWallet);

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
        // Roster subject is an ORG DID embedding the org id; caller has no wallet claim.
        // Unchanged by #1620 — this pre-existing path stays ungated. See the note in
        // SubjectMatchesCaller: adding a role check here is a separate decision, and it failed 24
        // integration tests whose callers legitimately publish through this match today.
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
    // -------------------------------------------------------------------------
    // #1620 — a register owned by the ORGANISATION's signing wallet
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OrgAdmin_PublishesToARegisterOwnedByTheOrgWallet()
    {
        // The register the UI creates after #1525: its only roster entry is the ORG's signing
        // wallet, and no user token carries that address. Before #1620 this refused everyone,
        // including the admin who created the wallet.
        SetRoster(Member($"did:sorcha:w:{OrgWallet}", "Owner"));
        OrgResolvesToItsWallet();

        var decision = await CreateGate().EvaluateAsync(
            OrgAdminWithoutOwnWallet(), BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.ProceedWithOverride,
            "an administrator acting for the organisation that OWNS the register must be able to publish to it");
    }

    [Fact]
    public async Task OrdinaryOrgMember_IsStillRefused_EvenThoughTheirOrgOwnsTheRegister()
    {
        // The over-admission the role gate exists to close: org-wallet authority belongs to the
        // ORGANISATION, so honouring it for any member would hand publish rights to every user.
        SetRoster(Member($"did:sorcha:w:{OrgWallet}", "Owner"));
        OrgResolvesToItsWallet();

        var decision = await CreateGate().EvaluateAsync(
            OrdinaryOrgMember(), BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden,
            "membership of the owning organisation is not itself publish authority");
    }

    [Fact]
    public async Task OrdinaryOrgMember_IsNotEvenLookedUp()
    {
        SetRoster(Member($"did:sorcha:w:{OrgWallet}", "Owner"));
        OrgResolvesToItsWallet();

        await CreateGate().EvaluateAsync(
            OrdinaryOrgMember(), BlueprintId, RegisterId, overrideConfirmed: true);

        _orgInfoClient.Verify(
            c => c.ResolveCanonicalWalletAddressAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a caller who could not use the result must not trigger the lookup at all");
    }

    [Fact]
    public async Task OrgWalletThatIsNotOnTheRoster_DoesNotGrantPublish()
    {
        // Resolving an org wallet is not authority in itself — it must actually be attested.
        SetRoster(Member("did:sorcha:w:ws11q-some-other-orgs-wallet", "Owner"));
        OrgResolvesToItsWallet();

        var decision = await CreateGate().EvaluateAsync(
            OrgAdminWithoutOwnWallet(), BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden);
    }

    [Fact]
    public async Task WhenTheOrgLookupFails_TheGateRefuses_RatherThanAdmitting()
    {
        SetRoster(Member($"did:sorcha:w:{OrgWallet}", "Owner"));
        _orgInfoClient
            .Setup(c => c.ResolveCanonicalWalletAddressAsync(OrgGuid, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("tenant unreachable"));

        var decision = await CreateGate().EvaluateAsync(
            OrgAdminWithoutOwnWallet(), BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden,
            "an unresolvable organisation must never resolve in the permissive direction");
    }

    [Fact]
    public async Task AnIndividuallyAttestedCaller_NeedsNoOrgRole()
    {
        // Unchanged behaviour: being named on the roster IS the authority. The role gate applies
        // only to the organisational match.
        SetRoster(Member($"did:sorcha:w:{CallerWallet}", "Designer"));

        var caller = AuthorisedCaller() with { HoldsOrgPublishRole = false };
        var decision = await CreateGate().EvaluateAsync(
            caller, BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.ProceedWithOverride);
        _orgInfoClient.Verify(
            c => c.ResolveCanonicalWalletAddressAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ARosterEntryWithoutAPublishingRole_StillDoesNotGrantPublish()
    {
        // The org wallet is on the roster, but as a role that confers no publish authority.
        SetRoster(Member($"did:sorcha:w:{OrgWallet}", "Auditor"));
        OrgResolvesToItsWallet();

        var decision = await CreateGate().EvaluateAsync(
            OrgAdminWithoutOwnWallet(), BlueprintId, RegisterId, overrideConfirmed: true);

        decision.Outcome.Should().Be(PublishGateOutcome.Forbidden);
    }

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
