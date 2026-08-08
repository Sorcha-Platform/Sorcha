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
/// An approval payload must read back as what was written (Feature 189).
/// </summary>
/// <remarks>
/// <para>
/// <b>Found live on n1.</b> The payload was written with
/// <see cref="GovernanceApprovalActionPayload.CanonicalJsonOptions"/> — whose enums are kebab-case —
/// and read back by both the enactment service and the Validator with ad-hoc options carrying no
/// converters. The deserialise <i>threw</i>, and every reader wraps the decode in a try/catch that
/// treats a throw as "this is not an approval". So both sides saw <b>zero</b> approvals, quorum was
/// never met, and nothing was ever enacted — with no error anywhere, because the failure was
/// swallowed by design.
/// </para>
/// <para>
/// The write side was tested. The read side was tested. Nothing tested the join, which is the whole
/// shape of this class of defect.
/// </para>
/// </remarks>
public sealed class GovernanceApprovalPayloadRoundTripTests
{
    private static GovernanceApprovalActionPayload FullyPopulated() => new()
    {
        ProposalId = "83bc29d3a2ede83d060278f71af9d70d",
        ApproverDid = "did:sorcha:w:ws11qapprover",
        IsApproval = false,
        Signature = "fFE+9QNpjWLk9+hPDXbfIFctbmex6ONxaOnMVUAkjWA=",
        PublicKey = "uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc=",
        AuthMethod = ApprovalAuthMethod.HardwareBacked,
        Comment = "round trip",
        Authorisation = new ApprovalAuthorisation
        {
            Kind = AuthorisationKind.Delegated,
            IndividualDid = "did:sorcha:w:ws11qindividual",
            Signature = "aW5kaXZpZHVhbA==",
            PublicKey = "Ym90a2V5",
            AuthMethod = ApprovalAuthMethod.Service,
            Algorithm = SignatureAlgorithm.ED25519,
            DelegationAlgorithm = SignatureAlgorithm.ED25519,
            DelegationSignature = "Z3JhbnQ=",
            DelegationPublicKey = "Z3JhbnRvcmtleQ==",
            Delegation = new GovernanceDelegation
            {
                DelegationId = "delegation-1",
                OrganisationDid = "did:sorcha:w:ws11qapprover",
                IndividualDid = "did:sorcha:w:ws11qindividual",
                ApproverPublicKey = "Ym90a2V5",
                Scope = [GovernanceOperationType.CryptoPolicyUpdate],
                ExpiresAt = DateTimeOffset.UnixEpoch.AddDays(30),
                GrantedAt = DateTimeOffset.UnixEpoch,
            },
        },
    };

    private static GovernanceApprovalActionPayload RoundTrip(GovernanceApprovalActionPayload payload)
    {
        var wire = JsonSerializer.Serialize(payload, GovernanceApprovalActionPayload.CanonicalJsonOptions);
        return JsonSerializer.Deserialize<GovernanceApprovalActionPayload>(
            wire, GovernanceApprovalActionPayload.CanonicalJsonOptions)!;
    }

    [Fact]
    public void WhatIsWritten_ReadsBackAsTheSameThing()
    {
        RoundTrip(FullyPopulated()).Should().BeEquivalentTo(FullyPopulated(),
            "the canonical options are the read contract as well as the write contract");
    }

    [Fact]
    public void TheEnumsSurvive_WhichIsWhatAdHocOptionsThrowOn()
    {
        var back = RoundTrip(FullyPopulated());

        back.AuthMethod.Should().Be(ApprovalAuthMethod.HardwareBacked);
        back.Authorisation!.Kind.Should().Be(AuthorisationKind.Delegated);
        back.Authorisation.AuthMethod.Should().Be(ApprovalAuthMethod.Service);
        back.Authorisation.Delegation!.Scope.Should().ContainSingle()
            .Which.Should().Be(GovernanceOperationType.CryptoPolicyUpdate);
    }

    [Fact]
    public void TheDiscriminatorSurvives_SoAReaderCanStillRecogniseIt()
    {
        // Every reader filters on this before doing anything else. If it does not survive the round
        // trip the approval is discarded as some other kind of transaction.
        RoundTrip(FullyPopulated()).Type
            .Should().Be(GovernanceApprovalActionPayload.PayloadType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheVoteSurvives(bool isApproval)
    {
        var payload = FullyPopulated();
        payload.IsApproval = isApproval;

        RoundTrip(payload).IsApproval.Should().Be(isApproval,
            "a rejection read back as an approval would invert a vote");
    }
}
