// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Every governance transaction is individually attributable to the organisation behind it, from
/// SIGNED content (FR-019 / T056).
/// </summary>
/// <remarks>
/// <para>
/// "Attributable" has to mean attributable to something an attacker cannot rewrite. A transaction's
/// <c>Metadata</c> sits outside the signature, outside the payload hash and outside the docket's
/// merkle leaf, so anyone able to submit can change it with nothing detecting the change — the same
/// property that made the old proposals list untrustworthy. So the test is not "the DID appears
/// somewhere", it is "the DID is inside the bytes a signature commits to".
/// </para>
/// <para>
/// Each case mutates the attributing field and asserts the committed bytes change. That is the
/// property, stated directly: if the identity can be swapped without disturbing what was signed, the
/// ledger records an act by one organisation as an act by another.
/// </para>
/// </remarks>
public sealed class GovernanceAttributionTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";
    private const string ProposerDid = "did:sorcha:w:ws11qproposer";
    private const string ImpostorDid = "did:sorcha:w:ws11qimpostor";

    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private static GovernanceOperation Operation(string proposerDid = ProposerDid) => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = proposerDid,
        TargetDid = "did:sorcha:w:ws11qnew",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = Now,
        ExpiresAt = Now.AddDays(7),
        RosterSnapshotId = "genesis-tx",
    };

    /// <summary>The bytes a control transaction's signature commits to, via its payload hash.</summary>
    private static string CanonicalPayload(GovernanceOperation operation, RegisterControlRecord? roster = null)
        => JsonSerializer.Serialize(
            new ControlTransactionPayload { Version = 1, Roster = roster, Operation = operation },
            new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

    // ---- the proposal ----

    [Fact]
    public void AProposal_NamesTheOrganisationThatRaisedIt_InTheSignedPayload()
    {
        CanonicalPayload(Operation()).Should().Contain(ProposerDid,
            "the proposer is inside the bytes the payload hash covers, not in rewritable metadata");
    }

    [Fact]
    public void AProposal_CannotBeReattributedWithoutChangingWhatWasSigned()
    {
        CanonicalPayload(Operation())
            .Should().NotBe(CanonicalPayload(Operation(ImpostorDid)));
    }

    // ---- the approval ----

    /// <summary>
    /// The approver is bound twice over: in the payload the transaction hashes, and in the statement
    /// digest their own detached signature covers. The second is what makes it un-reattributable even
    /// by the node carrying it.
    /// </summary>
    [Fact]
    public void AnApproval_BindsTheApproverInTheStatementItsOwnSignatureCovers()
    {
        var operation = Operation();

        var mine = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, ProposerDid, true);
        var theirs = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, ImpostorDid, true);

        mine.Should().NotEqual(theirs,
            "an approval re-pointed at another organisation must stop verifying");
    }

    [Fact]
    public void AnApproval_NamesTheApprovingOrganisation_InTheSignedPayload()
    {
        var payload = new GovernanceApprovalActionPayload
        {
            ProposalId = "proposal-tx",
            ApproverDid = ProposerDid,
            IsApproval = true,
            Signature = "c2ln",
            PublicKey = "cHVi",
            AuthMethod = ApprovalAuthMethod.HardwareBacked,
        };

        JsonSerializer.Serialize(payload, GovernanceApprovalActionPayload.CanonicalJsonOptions)
            .Should().Contain(ProposerDid);
    }

    /// <summary>
    /// FR-029: an approval also resolves to a named individual, and that link is inside the payload
    /// too — an accountability record in metadata would be a self-declaration anyone could edit.
    /// </summary>
    [Fact]
    public void AnApproval_CarriesTheAccountableIndividual_InTheSignedPayload()
    {
        var payload = new GovernanceApprovalActionPayload
        {
            ProposalId = "proposal-tx",
            ApproverDid = ProposerDid,
            IsApproval = true,
            Signature = "c2ln",
            PublicKey = "cHVi",
            Authorisation = new ApprovalAuthorisation
            {
                Kind = AuthorisationKind.Direct,
                IndividualDid = "did:sorcha:w:ws11qperson",
                Signature = "c2ln",
                PublicKey = "cHVi",
            },
        };

        JsonSerializer.Serialize(payload, GovernanceApprovalActionPayload.CanonicalJsonOptions)
            .Should().Contain("did:sorcha:w:ws11qperson");
    }

    // ---- the enactment ----

    /// <summary>
    /// An enactment carries the operation forward as proposed, so it stays attributable to the
    /// organisation that raised the change even though a different node may have carried it.
    /// </summary>
    /// <remarks>
    /// This is the distinction the whole carry/authority separation rests on: <i>who carried it</i>
    /// confers nothing, and <i>who proposed it</i> plus the approvals that authorised it are the
    /// record. Both are on the ledger as signed content, in this transaction and in the approval
    /// transactions chained to the proposal.
    /// </remarks>
    [Fact]
    public void AnEnactment_StillNamesTheOrganisationThatRaisedTheChange()
    {
        var enacted = Operation();
        enacted.Status = ProposalStatus.Recorded;

        var roster = new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "Test",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Attestations = [],
        };

        CanonicalPayload(enacted, roster).Should().Contain(ProposerDid);
    }

    /// <summary>
    /// The three kinds must remain distinguishable on the ledger, or "attributable" collapses: an
    /// approval counted as a proposal, or an enactment counted as one, attributes an act to an
    /// organisation that did not perform it. Keyed on payload content, never on metadata.
    /// </summary>
    [Fact]
    public void TheThreeKinds_AreDistinguishableFromPayloadContentAlone()
    {
        var proposal = CanonicalPayload(Operation());

        var enacted = Operation();
        enacted.Status = ProposalStatus.Recorded;
        var enactment = JsonSerializer.Serialize(
            new ControlTransactionPayload
            {
                Version = 1,
                Roster = new RegisterControlRecord
                {
                    RegisterId = RegisterId, Name = "Test",
                    CreatedAt = DateTimeOffset.UnixEpoch, Attestations = [],
                },
                Operation = enacted,
                EnactsProposalId = "proposal-tx",
            },
            new JsonSerializerOptions { WriteIndented = false });

        var approval = JsonSerializer.Serialize(
            new GovernanceApprovalActionPayload
            {
                ProposalId = "proposal-tx",
                ApproverDid = ProposerDid,
                Signature = "c2ln",
                PublicKey = "cHVi",
            },
            GovernanceApprovalActionPayload.CanonicalJsonOptions);

        // A proposal carries no roster and enacts nothing. Note the discriminator is a null VALUE, not
        // an absent property — the field is serialised either way, so a reader keying on "is the key
        // present" would classify every proposal as an enactment.
        proposal.Should().Contain("\"roster\":null").And.Contain("\"enactsProposalId\":null");
        enactment.Should().Contain("\"enactsProposalId\":\"proposal-tx\"");
        approval.Should().Contain($"\"type\":\"{GovernanceApprovalActionPayload.PayloadType}\"");
    }

    /// <summary>
    /// Nothing may be attributed from <c>Metadata</c>. It is outside the signature, outside the
    /// payload hash and outside the merkle leaf, so a value there is a hint for operators and never
    /// evidence — <c>carriedBy</c> in particular records which node carried a transaction and is
    /// deliberately read by nothing.
    /// </summary>
    [Fact]
    public void TheAttributingFields_AreNotInMetadata()
    {
        var payload = CanonicalPayload(Operation());

        // If the proposer ever moves to metadata this fails, which is the point: the day it becomes
        // a metadata field is the day attribution becomes forgeable.
        payload.Should().Contain(ProposerDid);
        typeof(GovernanceOperation).GetProperties()
            .Select(p => p.Name)
            .Should().Contain("ProposerDid",
                "attribution belongs on the payload model, never on the transaction envelope");
    }
}
