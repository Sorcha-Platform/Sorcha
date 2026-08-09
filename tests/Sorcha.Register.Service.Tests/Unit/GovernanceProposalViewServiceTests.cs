// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// The governance audit surface (T043/T046) — everything derived from sealed content.
/// </summary>
/// <remarks>
/// <para>
/// The pure derivation is covered by <c>GovernanceProposalStatusTests</c>. What is tested here is the
/// half that reads the ledger: that the enactment is found by the same deterministic id the enactment
/// service writes, that approvals are attributed to their own transactions, and that approvals which
/// cannot count are reported with reasons rather than dropped.
/// </para>
/// <para>
/// Payloads are built through the real serialisers, not hand-written JSON, so a change to how an
/// approval is written cannot leave these tests asserting a shape nothing produces.
/// </para>
/// </remarks>
public sealed class GovernanceProposalViewServiceTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";
    private const string ProposalTx = "proposal-tx";
    private const string GenesisHead = "genesis-tx";
    private const string OwnerDid = "did:sorcha:w:ws11qowner";
    private const string AdminDid = "did:sorcha:w:ws11qadmin";
    private const string StrangerDid = "did:sorcha:w:ws11qstranger";

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IGovernanceRosterService> _roster = new();
    private readonly Mock<IReadOnlyRegisterRepository> _repository = new();
    private readonly TimeProvider _clock = new FixedClock(Now);

    /// <summary>A clock that does not move, so an expiry test cannot pass by timing.</summary>
    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private static string Key(byte seed) => Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray());

    private static RegisterAttestation Attestation(string did, RegisterRole role, string key) => new()
    {
        Subject = did,
        Role = role,
        PublicKey = key,
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = DateTimeOffset.UnixEpoch,
    };

    private static AdminRoster Roster(string head = GenesisHead) => new()
    {
        RegisterId = RegisterId,
        ControlRecord = new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "Test",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Attestations =
            [
                Attestation(OwnerDid, RegisterRole.Owner, Key(1)),
                Attestation(AdminDid, RegisterRole.Admin, Key(2)),
            ]
        },
        ControlTransactionCount = 1,
        LastControlTxId = head,
    };

    private static GovernanceOperation Operation() => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = AdminDid,
        TargetDid = "did:sorcha:w:ws11qnew",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = Now.AddHours(-2),
        ExpiresAt = Now.AddDays(7),
        RosterSnapshotId = GenesisHead,
        QuorumFormulaAtRaise = QuorumFormula.Unanimous,
    };

    private static TransactionModel ProposalTransaction() => new()
    {
        TxId = ProposalTx,
        RegisterId = RegisterId,
        TimeStamp = Now.AddHours(-2).UtcDateTime,
        MetaData = new TransactionMetaData
        {
            TrackingData = new Dictionary<string, string> { ["transactionType"] = "GovernanceOperation" }
        },
        Payloads =
        [
            new PayloadModel
            {
                Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new ControlTransactionPayload { Version = 1, Roster = null, Operation = Operation() }))
            }
        ],
    };

    private static TransactionModel ApprovalTransaction(
        string approverDid, string publicKey, string txId, string? individualDid = "did:sorcha:w:ws11qperson")
        => new()
        {
            TxId = txId,
            RegisterId = RegisterId,
            DocketNumber = 2,
            TimeStamp = Now.AddMinutes(-10).UtcDateTime,
            Payloads =
            [
                new PayloadModel
                {
                    Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                        new GovernanceApprovalActionPayload
                        {
                            ProposalId = ProposalTx,
                            ApproverDid = approverDid,
                            IsApproval = true,
                            Signature = "c2ln",
                            PublicKey = publicKey,
                            AuthMethod = ApprovalAuthMethod.HardwareBacked,
                            Authorisation = individualDid is null ? null : new ApprovalAuthorisation
                            {
                                Kind = AuthorisationKind.Direct,
                                IndividualDid = individualDid,
                                Signature = "c2ln",
                                PublicKey = Key(9),
                            },
                        },
                        GovernanceApprovalActionPayload.CanonicalJsonOptions))
                }
            ],
        };

    /// <summary>The fixture with the proposed operation reshaped — Remove, Transfer, policy, …</summary>
    private GovernanceProposalViewService ServiceForOperation(Action<GovernanceOperation> shape)
    {
        var operation = Operation();
        shape(operation);

        var tx = ProposalTransaction();
        tx.Payloads[0].Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new ControlTransactionPayload { Version = 1, Roster = null, Operation = operation }));

        var service = Service();
        _repository.Setup(r => r.GetTransactionAsync(RegisterId, ProposalTx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);
        _repository.Setup(r => r.GetTransactionsByTypeAsync(
                RegisterId, TransactionType.Control, It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        return service;
    }

    /// <summary>
    /// A separate enactment: carries the resulting roster AND names the proposal it enacts.
    /// </summary>
    private static TransactionModel EnactmentTransaction(string enactsProposalId) => new()
    {
        TxId = "enactment-tx",
        RegisterId = RegisterId,
        TimeStamp = Now.AddMinutes(-5).UtcDateTime,
        MetaData = new TransactionMetaData
        {
            TrackingData = new Dictionary<string, string> { ["transactionType"] = "GovernanceOperation" }
        },
        Payloads =
        [
            new PayloadModel
            {
                Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new ControlTransactionPayload
                    {
                        Version = 1,
                        Roster = Roster().ControlRecord,
                        Operation = Recorded(),
                        EnactsProposalId = enactsProposalId,
                    }))
            }
        ],
    };

    /// <summary>The Owner override: one propose-and-enact transaction, enacting nothing else.</summary>
    private static TransactionModel ProposeAndEnactTransaction() => new()
    {
        TxId = "override-tx",
        RegisterId = RegisterId,
        TimeStamp = Now.AddMinutes(-5).UtcDateTime,
        MetaData = new TransactionMetaData
        {
            TrackingData = new Dictionary<string, string> { ["transactionType"] = "GovernanceOperation" }
        },
        Payloads =
        [
            new PayloadModel
            {
                Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new ControlTransactionPayload
                    {
                        Version = 1,
                        Roster = Roster().ControlRecord,
                        Operation = Recorded(),
                        EnactsProposalId = null,
                    }))
            }
        ],
    };

    private static GovernanceOperation Recorded()
    {
        var o = Operation();
        o.Status = ProposalStatus.Recorded;
        return o;
    }

    private GovernanceProposalViewService Service(params TransactionModel[] approvals)
    {
        _roster.Setup(r => r.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster());
        _roster.Setup(r => r.ValidateQuorumAsync(
                RegisterId, It.IsAny<GovernanceOperation>(),
                It.IsAny<List<ApprovalSignature>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, GovernanceOperation _, List<ApprovalSignature> votes, CancellationToken _) =>
                new QuorumResult
                {
                    IsQuorumMet = votes.Count >= 2,
                    VotesRequired = 2,
                    VotesReceived = votes.Count,
                    VotingPool = 2,
                });

        // ApplyOperation delegates to the REAL GovernanceRosterService. Restating the roster rules in
        // the test would prove only that the test agrees with itself — and the whole point of T084 is
        // that the preview is computed by the same code that writes the enactment.
        var realRoster = new GovernanceRosterService(
            _repository.Object, NullLogger<GovernanceRosterService>.Instance);
        _roster.Setup(r => r.ApplyOperation(
                It.IsAny<RegisterControlRecord>(), It.IsAny<GovernanceOperation>(), It.IsAny<RegisterAttestation?>()))
            .Returns((RegisterControlRecord c, GovernanceOperation o, RegisterAttestation? a)
                => realRoster.ApplyOperation(c, o, a));

        _repository.Setup(r => r.GetTransactionAsync(RegisterId, ProposalTx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProposalTransaction());
        _repository.Setup(r => r.GetTransactionsByTypeAsync(
                RegisterId, TransactionType.Control, It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ProposalTransaction()]);
        _repository.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                RegisterId, ProposalTx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(approvals);

        var reader = new GovernanceProposalReader(
            _repository.Object, NullLogger<GovernanceProposalReader>.Instance);

        return new GovernanceProposalViewService(reader, _roster.Object, _repository.Object, _clock);
    }

    /// <summary>Nothing has enacted it, so it is Open with no reason and no outcome transaction.</summary>
    [Fact]
    public async Task AProposalWithNoEnactment_IsReportedOpen()
    {
        var view = await Service().GetAsync(RegisterId, ProposalTx);

        view.Should().NotBeNull();
        view!.Status.Should().Be(GovernanceProposalState.Open);
        view.StatusReason.Should().Be(GovernanceProposalStateReason.None);
        view.OutcomeTxId.Should().BeNull();
        view.QuorumFormula.Should().Be(QuorumFormula.Unanimous, "the rule captured at raise time");
    }

    /// <summary>
    /// The enactment is found by the id <c>GovernanceEnactmentService</c> writes, so the two cannot
    /// disagree about which transaction enacted which proposal.
    /// </summary>
    [Fact]
    public async Task AnEnactedProposal_IsFoundByTheDeterministicEnactmentId()
    {
        var service = Service();
        var enactmentId = GovernanceEnactmentService.ComputeEnactmentTransactionId(RegisterId, ProposalTx);

        _repository.Setup(r => r.GetTransactionAsync(RegisterId, enactmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionModel { TxId = enactmentId, RegisterId = RegisterId });

        var view = await service.GetAsync(RegisterId, ProposalTx);

        view!.Status.Should().Be(GovernanceProposalState.Enacted);
        view.StatusReason.Should().Be(GovernanceProposalStateReason.QuorumMet);
        view.OutcomeTxId.Should().Be(enactmentId);
    }

    [Fact]
    public async Task EachApproval_IsAttributedToItsOwnTransaction()
    {
        var view = await Service(
                ApprovalTransaction(OwnerDid, Key(1), "approval-owner"),
                ApprovalTransaction(AdminDid, Key(2), "approval-admin"))
            .GetAsync(RegisterId, ProposalTx);

        view!.Approvals.Should().HaveCount(2);
        view.Approvals.Select(a => a.TxId).Should().BeEquivalentTo(["approval-owner", "approval-admin"],
            "an auditor must be able to read the evidence, not this summary of it");
        view.Approvals.Should().OnlyContain(a => a.AccountableIndividualDid == "did:sorcha:w:ws11qperson");
        view.Approvals.Should().OnlyContain(a => a.AuthMethod == "HardwareBacked");
        view.ApprovalsReceived.Should().Be(2);
        view.ApprovalsRequired.Should().Be(2);
    }

    /// <summary>
    /// FR-011c: an approval that cannot count is reported with its reason. One that vanished from the
    /// report would look exactly like one that was never submitted.
    /// </summary>
    [Fact]
    public async Task AnApprovalThatCannotCount_IsReportedWithItsReason()
    {
        var view = await Service(
                ApprovalTransaction(OwnerDid, Key(1), "approval-owner"),
                ApprovalTransaction(StrangerDid, Key(7), "approval-stranger"))
            .GetAsync(RegisterId, ProposalTx);

        view!.Approvals.Should().ContainSingle().Which.ApproverDid.Should().Be(OwnerDid);
        view.ExcludedApprovals.Should().ContainSingle()
            .Which.Should().Match<GovernanceExcludedApprovalView>(
                e => e.ApproverDid == StrangerDid
                     && e.Reason == ApprovalTallyRefusal.NotOnRoster
                     && e.TxId == "approval-stranger");
    }

    /// <summary>
    /// An approval offering a key the roster does not record for that organisation is excluded — the
    /// roster is the authority on which key an organisation governs with, never the payload.
    /// </summary>
    [Fact]
    public async Task AnApprovalOfferingTheWrongKey_IsExcluded()
    {
        var view = await Service(ApprovalTransaction(OwnerDid, Key(99), "approval-owner"))
            .GetAsync(RegisterId, ProposalTx);

        view!.Approvals.Should().BeEmpty();
        view.ExcludedApprovals.Should().ContainSingle()
            .Which.Reason.Should().Be(ApprovalTallyRefusal.KeyNotTheRosterKey);
    }

    [Fact]
    public async Task AnUnknownProposal_IsNotFound()
    {
        var service = Service();
        _repository.Setup(r => r.GetTransactionAsync(RegisterId, "nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        (await service.GetAsync(RegisterId, "nope")).Should().BeNull();
    }

    [Fact]
    public async Task TheList_FiltersOnTheDerivedState()
    {
        var service = Service();

        (await service.ListAsync(RegisterId, GovernanceProposalState.Open))
            .Should().ContainSingle().Which.ProposalId.Should().Be(ProposalTx);

        (await service.ListAsync(RegisterId, GovernanceProposalState.Enacted))
            .Should().BeEmpty("nothing has enacted it");

        (await service.ListAsync(RegisterId, state: null))
            .Should().ContainSingle("no filter means every proposal");
    }

    // ---- an enactment is the OUTCOME of a proposal, not a proposal ----

    /// <summary>
    /// Found live on n1: the enactment transaction was listed as a proposal of its own, so one
    /// governance change appeared twice — once as the proposal and once as its own outcome, the
    /// second row showing 0 approvals because approvals chain off the proposal, not the enactment.
    /// </summary>
    [Fact]
    public async Task AnEnactment_IsNotListedAsAProposalOfItsOwn()
    {
        var service = Service();
        _repository.Setup(r => r.GetTransactionsByTypeAsync(
                RegisterId, TransactionType.Control, It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ProposalTransaction(), EnactmentTransaction(ProposalTx)]);

        var list = await service.ListAsync(RegisterId, state: null);

        list.Should().ContainSingle("the change is one proposal, whatever settled it")
            .Which.ProposalId.Should().Be(ProposalTx);
    }

    [Fact]
    public async Task AnEnactment_HasNoDetailPageOfItsOwn()
    {
        var service = Service();
        _repository.Setup(r => r.GetTransactionAsync(RegisterId, "enactment-tx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EnactmentTransaction(ProposalTx));

        (await service.GetAsync(RegisterId, "enactment-tx")).Should().BeNull(
            "it is reachable as the outcomeTxId of the proposal it enacted");
    }

    /// <summary>
    /// The Owner override writes ONE transaction that is both proposal and enactment. Excluding
    /// everything that carries a roster would drop single-owner governance from the audit surface
    /// entirely — so the discriminator is naming ANOTHER proposal, not carrying a roster.
    /// </summary>
    [Fact]
    public async Task AnOwnerOverrideProposeAndEnact_IsStillListed()
    {
        var service = Service();
        _repository.Setup(r => r.GetTransactionsByTypeAsync(
                RegisterId, TransactionType.Control, It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ProposeAndEnactTransaction()]);

        var list = await service.ListAsync(RegisterId, state: null);

        var only = list.Should().ContainSingle().Subject;
        only.ProposalId.Should().Be("override-tx");
        only.Status.Should().Be(GovernanceProposalState.Enacted);
        only.OutcomeTxId.Should().Be("override-tx", "it is its own outcome");
    }

    // ---- T084: the roster diff an approver reads before approving ----

    /// <summary>
    /// An approver must be able to read what they are authorising, so the detail carries the roster
    /// as it will be — computed by the SAME projection the enactment writes (FR-027).
    /// </summary>
    [Fact]
    public async Task AnOpenAddProposal_CarriesTheRosterDiff()
    {
        var view = await Service().GetAsync(RegisterId, ProposalTx);

        view!.RosterDiff.Should().NotBeNull();
        view.RosterDiff!.Should().HaveCount(3, "two sitting members plus the one being added");

        view.RosterDiff.Should().ContainSingle(m => m.Change == GovernanceRosterChange.Added)
            .Which.Subject.Should().Be("did:sorcha:w:ws11qnew");
        view.RosterDiff.Where(m => m.Change == GovernanceRosterChange.Unchanged)
            .Select(m => m.Subject).Should().BeEquivalentTo([OwnerDid, AdminDid]);
    }

    [Fact]
    public async Task AnOpenRemoveProposal_MarksTheDepartingMember()
    {
        var view = await ServiceForOperation(op =>
        {
            op.OperationType = GovernanceOperationType.Remove;
            op.TargetDid = AdminDid;
        }).GetAsync(RegisterId, ProposalTx);

        view!.RosterDiff.Should().ContainSingle(m => m.Change == GovernanceRosterChange.Removed)
            .Which.Subject.Should().Be(AdminDid);
        view.RosterDiff!.Should().HaveCount(2, "the removed member is still shown, marked as leaving");
    }

    /// <summary>
    /// A Transfer moves the ownership, so BOTH parties change role. Showing only the new Owner would
    /// hide from the sitting Owner that they are the one being demoted.
    /// </summary>
    [Fact]
    public async Task AnOpenTransferProposal_ShowsBothRoleChanges()
    {
        var view = await ServiceForOperation(op =>
        {
            op.OperationType = GovernanceOperationType.Transfer;
            op.TargetDid = AdminDid;
        }).GetAsync(RegisterId, ProposalTx);

        view!.RosterDiff!.Where(m => m.Change == GovernanceRosterChange.RoleChanged)
            .Select(m => m.Subject).Should().BeEquivalentTo([OwnerDid, AdminDid]);

        view.RosterDiff.Single(m => m.Subject == AdminDid).Role.Should().Be(RegisterRole.Owner);
        view.RosterDiff.Single(m => m.Subject == OwnerDid).Role.Should().Be(RegisterRole.Admin);
    }

    /// <summary>
    /// An operation that changes no membership must not render an all-unchanged list — a diff shown
    /// for a change that is not a roster change tells the reader something untrue about it.
    /// </summary>
    [Fact]
    public async Task AProposalThatChangesNoMembership_CarriesNoRosterDiff()
    {
        var view = await ServiceForOperation(op =>
        {
            op.OperationType = GovernanceOperationType.CryptoPolicyUpdate;
            op.TargetDid = string.Empty;
        }).GetAsync(RegisterId, ProposalTx);

        view!.RosterDiff.Should().BeNull();
    }

    /// <summary>
    /// The diff is a preview of what approving would do. For a proposal that can no longer enact,
    /// projecting the operation onto the CURRENT roster would describe a change that will never
    /// happen — so it is withheld and the status carries the explanation instead.
    /// </summary>
    [Fact]
    public async Task AnInvalidatedProposal_CarriesNoRosterDiff()
    {
        var service = Service();
        _roster.Setup(r => r.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster(head: "the-roster-moved-on"));

        var view = await service.GetAsync(RegisterId, ProposalTx);

        view!.Status.Should().Be(GovernanceProposalState.Invalidated);
        view.RosterDiff.Should().BeNull();
    }

    /// <summary>
    /// An enacted proposal's outcome is the sealed transaction, not a recomputation. Re-projecting it
    /// onto the current roster would apply an already-applied change a second time.
    /// </summary>
    [Fact]
    public async Task AnEnactedProposal_CarriesNoRosterDiff()
    {
        var service = Service();
        var enactmentId = GovernanceEnactmentService.ComputeEnactmentTransactionId(RegisterId, ProposalTx);
        _repository.Setup(r => r.GetTransactionAsync(RegisterId, enactmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionModel { TxId = enactmentId, RegisterId = RegisterId });

        var view = await service.GetAsync(RegisterId, ProposalTx);

        view!.Status.Should().Be(GovernanceProposalState.Enacted);
        view.RosterDiff.Should().BeNull("the enactment transaction is the record");
        view.OutcomeTxId.Should().Be(enactmentId);
    }

    /// <summary>The list stays a summary: a diff per proposal would be unbounded work on a page load.</summary>
    [Fact]
    public async Task TheList_CarriesNoRosterDiff()
    {
        (await Service().ListAsync(RegisterId, state: null))
            .Single().RosterDiff.Should().BeNull();
    }

    /// <summary>
    /// The list is a summary — carrying every approval of every proposal would make it unbounded in
    /// the size of the register's governance history.
    /// </summary>
    [Fact]
    public async Task TheList_CarriesCountsButNotTheApprovalsThemselves()
    {
        var list = await Service(ApprovalTransaction(OwnerDid, Key(1), "approval-owner"))
            .ListAsync(RegisterId, state: null);

        var only = list.Single();
        only.ApprovalsReceived.Should().Be(1, "the counts are on the summary");
        only.Approvals.Should().BeEmpty("the detail endpoint carries them");
        only.ExcludedApprovals.Should().BeEmpty();
    }
}
