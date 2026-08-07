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

    private static AuthorisationValidationResult Validate(
        GovernanceApprovalSubmission submission,
        GovernanceOperation? operation = null,
        Func<string, bool>? isRevoked = null)
        => GovernanceAuthorisationValidator.Validate(
            RegisterId, operation ?? Operation(), submission, Now, isRevoked);

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
