// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Every approval must resolve to a named individual (FR-029 / R-017).
/// </summary>
/// <remarks>
/// The first design exempted autonomous approvers, on the grounds that a machine has no person behind
/// it. That was wrong: a machine external to the platform was <i>empowered by someone</i>, so
/// accountability is deferred, not absent. These tests pin that there is no path to the ledger that
/// skips it.
/// </remarks>
public sealed class GovernanceAuthorisationValidatorTests
{
    private const string RegisterId = "reg-1";
    private const string OrgDid = "did:sorcha:w:ws11qorg";
    private const string PersonDid = "did:sorcha:w:ws11qperson";
    private const string BotKey = "Qk9US0VZ";

    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static GovernanceOperation Operation(
        GovernanceOperationType type = GovernanceOperationType.CryptoPolicyUpdate) => new()
    {
        OperationType = type,
        ProposerDid = "did:sorcha:w:ws11qproposer",
        ProposedAt = Now.AddHours(-1),
    };

    private static GovernanceDelegation Delegation(
        IEnumerable<GovernanceOperationType>? scope = null,
        DateTimeOffset? expiresAt = null) => new()
    {
        DelegationId = "delegation-1",
        OrganisationDid = OrgDid,
        IndividualDid = PersonDid,
        ApproverPublicKey = BotKey,
        Scope = (scope ?? new[] { GovernanceOperationType.CryptoPolicyUpdate }).ToList(),
        GrantedAt = Now.AddDays(-1),
        ExpiresAt = expiresAt ?? Now.AddDays(30),
    };

    private static GovernanceApprovalSubmission Delegated(GovernanceDelegation delegation) => new()
    {
        RequestId = "req-1",
        ApproverDid = OrgDid,
        IsApproval = true,
        Signature = "T1JHU0lH",
        PublicKey = "T1JHS0VZ",
        AuthMethod = ApprovalAuthMethod.Service,
        Authorisation = new ApprovalAuthorisation
        {
            Kind = AuthorisationKind.Delegated,
            IndividualDid = PersonDid,
            Signature = "Qk9UU0lH",
            PublicKey = BotKey,
            AuthMethod = ApprovalAuthMethod.Service,
            Delegation = delegation,
            DelegationSignature = "REVMU0lH",
            DelegationPublicKey = "UEVSU09OS0VZ",
        },
    };

    /// <summary>
    /// Opts in to the delegated form (T095). Production never does — the default refuses it, because
    /// nothing can GRANT a delegation — but the machinery below it is correct and stays covered, so
    /// these tests say plainly that they are exercising a form the platform will not currently accept.
    /// <see cref="TheDelegatedForm_IsRefusedByDefault"/> pins the default itself.
    /// </summary>
    private static AuthorisationValidationResult Validate(
        GovernanceApprovalSubmission submission,
        GovernanceOperation? operation = null,
        Func<string, bool>? isRevoked = null)
        => GovernanceAuthorisationValidator.Validate(
            RegisterId, operation ?? Operation(), submission, Now, isRevoked, allowDelegated: true);

    // ---- FR-029: no approval reaches the ledger without an accountable person ----

    [Fact]
    public void ApprovalWithNoAuthorisation_IsRefused()
    {
        var result = Validate(new GovernanceApprovalSubmission { ApproverDid = OrgDid });

        result.IsAcceptable.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.Missing);
        result.AccountableIndividualDid.Should().BeNull();
    }

    [Fact]
    public void DelegatedApproval_ResolvesToTheEmpoweringIndividual()
    {
        // The whole point: a bot approval is still attributable to a person.
        var result = Validate(Delegated(Delegation()));

        result.IsAcceptable.Should().BeTrue();
        result.AccountableIndividualDid.Should().Be(PersonDid);
    }

    [Fact]
    public void DirectApproval_ResolvesToTheSigningIndividual()
    {
        var result = Validate(new GovernanceApprovalSubmission
        {
            ApproverDid = OrgDid,
            Authorisation = new ApprovalAuthorisation
            {
                Kind = AuthorisationKind.Direct,
                IndividualDid = PersonDid,
                Signature = "UEVSU0lH",
                PublicKey = "UEVSS0VZ",
                AuthMethod = ApprovalAuthMethod.HardwareBacked,
            },
        });

        result.IsAcceptable.Should().BeTrue();
        result.AccountableIndividualDid.Should().Be(PersonDid);
    }

    // ---- T089: scope ----

    [Fact]
    public void DelegationOutsideItsScope_IsRefused()
    {
        // The case that makes scoping worth having: a bot empowered for routine policy changes must
        // not be able to hand the register to someone else.
        var result = Validate(
            Delegated(Delegation(scope: new[] { GovernanceOperationType.CryptoPolicyUpdate })),
            Operation(GovernanceOperationType.Transfer));

        result.IsAcceptable.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.OutOfScope);
    }

    // ---- T090: expiry and revocation ----

    [Fact]
    public void ExpiredDelegation_IsRefused()
    {
        var result = Validate(Delegated(Delegation(expiresAt: Now.AddSeconds(-1))));

        result.IsAcceptable.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.Expired);
    }

    [Fact]
    public void RevokedDelegation_IsRefused()
    {
        var result = Validate(Delegated(Delegation()), isRevoked: id => id == "delegation-1");

        result.IsAcceptable.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.Revoked);
    }

    // ---- Binding the delegation to one organisation, one person, one key ----

    [Fact]
    public void DelegationForAnotherOrganisation_IsRefused()
    {
        var d = Delegation();
        d.OrganisationDid = "did:sorcha:w:ws11qother";

        Validate(Delegated(d)).Reason.Should().Be(AuthorisationRefusalReason.WrongOrganisation);
    }

    [Fact]
    public void DelegationNamingADifferentIndividual_IsRefused()
    {
        var d = Delegation();
        d.IndividualDid = "did:sorcha:w:ws11qsomeoneelse";

        Validate(Delegated(d)).Reason.Should().Be(AuthorisationRefusalReason.IndividualMismatch);
    }

    [Fact]
    public void DelegationQuotedByADifferentKey_IsRefused()
    {
        // Without this a valid delegation would authorise ANY machine able to quote it.
        var submission = Delegated(Delegation());
        submission.Authorisation!.PublicKey = "T1RIRVJLRVk=";

        Validate(submission).Reason.Should().Be(AuthorisationRefusalReason.ApproverKeyMismatch);
    }

    // ---- T095 / R-023: the delegated form is refused until granting exists -----------------------

    /// <summary>
    /// The default refuses a delegated approval outright, however well-formed it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing on the platform can <b>grant</b> a delegation — <c>GovernanceDelegation</c> is carried
    /// by a submission and verified by the validator, and issued by nothing — so a delegation
    /// arriving here cannot have been legitimately issued. Worse, nothing constrains who may claim to
    /// have issued one: the grant is checked to be signed by a key genuinely belonging to the named
    /// individual, but that individual is never matched against the roster, an Owner role, or even
    /// the organisation. R-023 says granting is Owner-only; there was no granting path to enforce it
    /// in.
    /// </para>
    /// <para>
    /// "No granting path" is not the same as "unreachable" — a hand-crafted submission needs no UI.
    /// So the form fails closed rather than being left as a self-assertable accountability record.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDelegatedForm_IsRefusedByDefault()
    {
        var submission = Delegated(Delegation());

        var result = GovernanceAuthorisationValidator.Validate(
            RegisterId, Operation(), submission, Now);

        result.IsAcceptable.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.DelegationNotAvailable);
        result.AccountableIndividualDid.Should().BeNull(
            "a refused approval resolves to nobody — it must not leave a name behind it");
    }

    /// <summary>
    /// The refusal comes BEFORE any delegation check, so nothing about the delegation's contents can
    /// change the answer.
    /// </summary>
    /// <remarks>
    /// A delegation that is expired, out of scope and revoked must be refused for the same reason as
    /// a perfect one. If the ordering were reversed, a caller could infer which of their fields the
    /// platform found acceptable — and, once granting lands, the more specific reason would be the
    /// honest one, so the ordering has to be deliberate rather than incidental.
    /// </remarks>
    [Fact]
    public void TheRefusal_PrecedesEveryDelegationCheck()
    {
        var rotten = Delegation();
        rotten.ExpiresAt = Now.AddYears(-1);
        rotten.Scope = [GovernanceOperationType.Transfer];      // not the operation under test
        rotten.OrganisationDid = "did:sorcha:w:ws11qsomebodyelse";

        var result = GovernanceAuthorisationValidator.Validate(
            RegisterId, Operation(), Delegated(rotten), Now, isRevoked: _ => true);

        result.Reason.Should().Be(AuthorisationRefusalReason.DelegationNotAvailable,
            "the form is unavailable, which is decided before anything about this particular "
            + "delegation is considered");
    }

    /// <summary>
    /// The refusal is confined to the delegated form — direct approvals are unaffected.
    /// </summary>
    /// <remarks>
    /// Without this, failing closed would be indistinguishable from having broken accountability
    /// altogether, and every governance approval on the platform would stop counting.
    /// </remarks>
    [Fact]
    public void ADirectApproval_IsUnaffectedByTheRefusal()
    {
        var result = GovernanceAuthorisationValidator.Validate(
            RegisterId, Operation(), new GovernanceApprovalSubmission
            {
                ApproverDid = OrgDid,
                Authorisation = new ApprovalAuthorisation
                {
                    Kind = AuthorisationKind.Direct,
                    IndividualDid = PersonDid,
                    Signature = "UEVSU0lH",
                    PublicKey = "UEVSS0VZ",
                    AuthMethod = ApprovalAuthMethod.HardwareBacked,
                },
            }, Now);

        result.IsAcceptable.Should().BeTrue(
            "the direct form is how every approval on the platform is currently made");
        result.Reason.Should().Be(AuthorisationRefusalReason.None);
    }

    [Fact]
    public void DelegatedKindWithNoDelegation_IsRefused()
    {
        var submission = Delegated(Delegation());
        submission.Authorisation!.Delegation = null;

        Validate(submission).Reason.Should().Be(AuthorisationRefusalReason.KindMismatch);
    }

    [Fact]
    public void DirectKindCarryingADelegation_IsRefused()
    {
        // Ambiguous provenance is refused rather than resolved by precedence.
        var submission = Delegated(Delegation());
        submission.Authorisation!.Kind = AuthorisationKind.Direct;

        Validate(submission).Reason.Should().Be(AuthorisationRefusalReason.KindMismatch);
    }

    // ---- What the crypto layer must still check ----

    [Fact]
    public void DelegatedApproval_RequiresBothSignaturesToBeVerified()
    {
        var result = Validate(Delegated(Delegation()));

        result.RequiredChecks.Select(c => c.Purpose)
            .Should().BeEquivalentTo(new[] { "authorisation", "delegation" },
                "the grant must be proven signed by the individual, not merely quoted");
    }

    [Fact]
    public void TheAuthorisationSignsTheSameStatementTheOrganisationDid()
    {
        // The individual commits to exactly what the organisation committed to — not a summary,
        // which could differ from what was enacted.
        var operation = Operation();
        var expected = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, OrgDid, true);

        var result = Validate(Delegated(Delegation()), operation);

        result.RequiredChecks.Single(c => c.Purpose == "authorisation").Digest.Should().Equal(expected);
    }

    [Fact]
    public void EveryRefusalCarriesAReason()
    {
        // FR-011c: nothing is silently dropped.
        var refusals = new[]
        {
            Validate(new GovernanceApprovalSubmission { ApproverDid = OrgDid }),
            Validate(Delegated(Delegation(expiresAt: Now.AddSeconds(-1)))),
            Validate(Delegated(Delegation(scope: Array.Empty<GovernanceOperationType>()))),
        };

        refusals.Should().OnlyContain(r => !r.IsAcceptable && r.Reason != AuthorisationRefusalReason.None);
    }
}
