// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Presentation;

/// <summary>
/// The wallet must be able to answer an <c>mso_mdoc</c> credential query (Feature 185).
/// </summary>
/// <remarks>
/// <para>
/// Before this, it could not. <c>ParseAsync</c> <b>threw</b> when a query carried no <c>vct_values</c>, and
/// matching compared <c>vct</c> strings only — so the wallet could not so much as <em>parse</em> a request for
/// the credential type that proximity presentation exists to present. The DCQL vocabulary (F181) has always
/// been able to express an mdoc query; the wallet simply could not answer one.
/// </para>
/// <para>
/// The load-bearing rule these tests pin: <b>the two formats have different identifiers and must never
/// cross-match.</b> An SD-JWT is found by <c>vct</c>, an mdoc by <c>docType</c>. Matching an mdoc request
/// against a <c>vct</c> (or vice versa) would present the wrong credential entirely.
/// </para>
/// </remarks>
public class MdocQueryMatchingTests
{
    private const string MdlDocType = "org.iso.18013.5.1.mDL";
    private const string IdentityVct = "https://sorcha.dev/vc/assured-identity/v1";

    private static PresentationEngine Engine() =>
        new(TimeProvider.System, NullLogger<PresentationEngine>.Instance);

    private static CachedCredential AnMdoc(string docType = MdlDocType) => new()
    {
        Id = Guid.NewGuid(),
        Format = CachedCredentialFormat.MsoMdoc,
        DocType = docType,
        IssuerSignedCbor = [0x01, 0x02, 0x03],
        AvailableClaimNames = ["family_name", "age_over_18", "birth_date"],
    };

    private static CachedCredential AnSdJwt(string vct = IdentityVct) => new()
    {
        Id = Guid.NewGuid(),
        Format = CachedCredentialFormat.SdJwtVc,
        Vct = vct,
        RawSdJwt = "header.payload.sig~disclosure~",
        AvailableClaimNames = ["family_name", "age_over_18"],
    };

    private static ParsedPresentationRequest MdocRequest(
        string docType = MdlDocType, params string[] claims) => new()
    {
        ClientId = "x509_san_dns:verifier.example",
        ResponseUri = "https://verifier.example/response",
        Nonce = "n0nce",
        State = "state-1",
        RequiredVct = docType,
        RequiredClaims = claims.Length > 0 ? claims : ["age_over_18"],
        OptionalClaims = [],
        Query = new DcqlQuery
        {
            Credentials =
            [
                new DcqlCredentialQuery
                {
                    Id = "mdl",
                    Format = DcqlFormats.MsoMdoc,
                    Meta = new DcqlCredentialMeta { DoctypeValue = docType },
                    Claims = (claims.Length > 0 ? claims : ["age_over_18"])
                        .Select(c => new DcqlClaimQuery { Path = [c] })
                        .ToList(),
                }
            ]
        },
    };

    /// <summary>An mdoc query finds the citizen's mdoc — the thing that was previously impossible.</summary>
    [Fact]
    public void AnMdocQuery_MatchesAnMdocCredential()
    {
        var matches = Engine().Match(MdocRequest(), [AnMdoc()]);

        matches.Should().HaveCount(1);
        matches[0].Credential.Format.Should().Be(CachedCredentialFormat.MsoMdoc);
        matches[0].Credential.DocType.Should().Be(MdlDocType);
        matches[0].SatisfiedRequired.Should().Equal(["age_over_18"]);
    }

    /// <summary>
    /// An mdoc query must <b>not</b> match an SD-JWT — even one whose <c>vct</c> happens to equal the docType.
    /// </summary>
    /// <remarks>
    /// This is the collision the format gate exists to prevent. Matching on the identifier alone would let a
    /// verifier asking for a driving licence be handed a completely different credential that merely shares a
    /// string.
    /// </remarks>
    [Fact]
    public void AnMdocQuery_DoesNotMatchAnSdJwt_EvenWithTheSameIdentifier()
    {
        var colliding = AnSdJwt(vct: MdlDocType);   // same string, different format

        var matches = Engine().Match(MdocRequest(), [colliding]);

        matches.Should().BeEmpty(because:
            "vct and docType are different namespaces of string — a request for an mdoc must never be " +
            "satisfied by an SD-JWT that happens to share the identifier");
    }

    /// <summary>And the converse: an SD-JWT query must not match an mdoc.</summary>
    [Fact]
    public void AnSdJwtQuery_DoesNotMatchAnMdoc()
    {
        var request = new ParsedPresentationRequest
        {
            ClientId = "x509_san_dns:verifier.example",
            ResponseUri = "https://verifier.example/response",
            Nonce = "n0nce",
            State = "state-1",
            RequiredVct = IdentityVct,
            RequiredClaims = ["age_over_18"],
            OptionalClaims = [],
            Query = new DcqlQuery
            {
                Credentials =
                [
                    new DcqlCredentialQuery
                    {
                        Id = "identity",
                        Format = DcqlFormats.SdJwtVc,
                        Meta = new DcqlCredentialMeta { VctValues = [IdentityVct] },
                        Claims = [new DcqlClaimQuery { Path = ["age_over_18"] }],
                    }
                ]
            },
        };

        var mdocWithTheSameIdentifier = AnMdoc(docType: IdentityVct);

        Engine().Match(request, [mdocWithTheSameIdentifier]).Should().BeEmpty();
    }

    /// <summary>A wallet holding both rails answers each query with the right credential.</summary>
    [Fact]
    public void AWalletHoldingBothFormats_AnswersAnMdocQueryWithTheMdoc()
    {
        var wallet = new List<CachedCredential> { AnSdJwt(), AnMdoc() };

        var matches = Engine().Match(MdocRequest(), wallet);

        matches.Should().HaveCount(1);
        matches[0].Credential.Format.Should().Be(CachedCredentialFormat.MsoMdoc);
    }

    /// <summary>An mdoc that lacks a requested element is not a match — no partial presentation.</summary>
    [Fact]
    public void AnMdocMissingARequiredElement_IsNotAMatch()
    {
        var request = MdocRequest(claims: ["age_over_21"]);   // the credential carries age_over_18 only

        Engine().Match(request, [AnMdoc()]).Should().BeEmpty();
    }

    /// <summary>
    /// A query with no identifier at all matches <b>nothing</b> — it must never fall through to "everything".
    /// </summary>
    /// <remarks>
    /// A permissive default here would offer the citizen credentials the verifier never asked for, which is
    /// the opposite of the minimal-disclosure property the whole design exists to provide.
    /// </remarks>
    [Fact]
    public void AQueryWithNoIdentifier_MatchesNothing()
    {
        var request = MdocRequest();
        var malformed = request with
        {
            RequiredVct = string.Empty,
            Query = new DcqlQuery
            {
                Credentials =
                [
                    new DcqlCredentialQuery
                    {
                        Id = "malformed",
                        Format = DcqlFormats.MsoMdoc,
                        Meta = new DcqlCredentialMeta(),   // neither doctype_value nor vct_values
                        Claims = [new DcqlClaimQuery { Path = ["age_over_18"] }],
                    }
                ]
            },
        };

        Engine().Match(malformed, [AnMdoc(), AnSdJwt()]).Should().BeEmpty(because:
            "an unidentifiable query must match nothing — never everything");
    }

    /// <summary>MatchQuery (the multi-credential DCQL path) handles an mdoc query too.</summary>
    [Fact]
    public void MatchQuery_SolvesAnMdocQuery()
    {
        var result = Engine().MatchQuery(MdocRequest(), [AnMdoc(), AnSdJwt()]);

        result.Satisfiable.Should().BeTrue();
        result.PerQuery.Should().HaveCount(1);
        result.PerQuery[0].Candidates.Should().HaveCount(1);
        result.PerQuery[0].Candidates[0].Credential.Format.Should().Be(CachedCredentialFormat.MsoMdoc);
        result.UnsatisfiedRequiredQueryIds.Should().BeEmpty();
    }
}
