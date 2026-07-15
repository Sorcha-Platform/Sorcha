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

namespace Sorcha.Wallet.Pwa.Tests.Services.Presentation;

/// <summary>
/// Feature 181 US2 (T024) — the per-query DCQL match + <c>credential_sets</c> solver:
/// two-query AND, two-option OR, required/optional sets, missing-credential block, multi-candidate.
/// </summary>
public class DcqlMatchTests
{
    private static readonly PresentationEngine Engine = new(TimeProvider.System, NullLogger<PresentationEngine>.Instance);

    private const string IdVct = "IdentityCredential";
    private const string AgeVct = "AgeCredential";

    // ── two-query AND (no credential_sets ⇒ all queries required) ─────────────

    [Fact]
    public void MatchQuery_TwoQueryAnd_BothPresent_Satisfiable()
    {
        var request = Req(Query(CQ("idq", IdVct, "given_name"), CQ("ageq", AgeVct, "age_over_18")));
        var result = Engine.MatchQuery(request, [Cred(IdVct, "given_name"), Cred(AgeVct, "age_over_18")]);

        result.Satisfiable.Should().BeTrue();
        result.PerQuery.Should().HaveCount(2);
        result.PerQuery.Should().OnlyContain(q => q.IsSatisfiable);
        result.UnsatisfiedRequiredQueryIds.Should().BeEmpty();
        result.SetChoices.Should().BeEmpty();
    }

    [Fact]
    public void MatchQuery_TwoQueryAnd_OneMissing_BlocksWithUnsatisfiedId()
    {
        var request = Req(Query(CQ("idq", IdVct, "given_name"), CQ("ageq", AgeVct, "age_over_18")));
        var result = Engine.MatchQuery(request, [Cred(IdVct, "given_name")]); // no AgeCredential

        result.Satisfiable.Should().BeFalse();
        result.UnsatisfiedRequiredQueryIds.Should().ContainSingle().Which.Should().Be("ageq");
        result.PerQuery.Single(q => q.QueryId == "ageq").Candidates.Should().BeEmpty();
    }

    [Fact]
    public void MatchQuery_RequiredClaimMissingOnCandidate_QueryUnsatisfiable()
    {
        var request = Req(Query(CQ("idq", IdVct, "given_name", "family_name")));
        var result = Engine.MatchQuery(request, [Cred(IdVct, "given_name")]); // missing family_name

        result.Satisfiable.Should().BeFalse();
        result.PerQuery.Single().Candidates.Should().BeEmpty();
    }

    // ── credential_sets (OR alternatives) ─────────────────────────────────────

    [Fact]
    public void MatchQuery_TwoOptionOr_OneSatisfiable_Satisfiable()
    {
        var query = Query(CQ("passport", IdVct, "given_name"), CQ("licence", AgeVct, "given_name"));
        var request = Req(query with { CredentialSets = [Set(required: true, ["passport"], ["licence"])] });

        var result = Engine.MatchQuery(request, [Cred(IdVct, "given_name")]); // only the passport option

        result.Satisfiable.Should().BeTrue();
        result.SetChoices.Should().ContainSingle();
        result.SetChoices[0].SatisfiableOptions.Should().ContainSingle()
            .Which.Should().ContainSingle().Which.Should().Be("passport");
    }

    [Fact]
    public void MatchQuery_RequiredSet_NoOptionSatisfiable_Blocks()
    {
        var query = Query(CQ("passport", IdVct, "given_name"), CQ("licence", AgeVct, "given_name"));
        var request = Req(query with { CredentialSets = [Set(required: true, ["passport"], ["licence"])] });

        var result = Engine.MatchQuery(request, []); // wallet holds neither

        result.Satisfiable.Should().BeFalse();
        result.SetChoices[0].SatisfiableOptions.Should().BeEmpty();
        result.SetChoices[0].IsMet.Should().BeFalse();
    }

    [Fact]
    public void MatchQuery_OptionalSet_Unmet_StillSatisfiable()
    {
        var query = Query(CQ("bonus", AgeVct, "age_over_18"));
        var request = Req(query with { CredentialSets = [Set(required: false, ["bonus"])] });

        var result = Engine.MatchQuery(request, []); // can't satisfy the optional set

        result.Satisfiable.Should().BeTrue();
        result.SetChoices[0].Required.Should().BeFalse();
        result.SetChoices[0].IsMet.Should().BeTrue(); // optional sets are always "met"
    }

    // ── multiple candidates for one query (the citizen chooses) ───────────────

    [Fact]
    public void MatchQuery_TwoMatchingCredentials_BothOfferedAsCandidates()
    {
        var request = Req(Query(CQ("idq", IdVct, "given_name")));
        var result = Engine.MatchQuery(request, [Cred(IdVct, "given_name"), Cred(IdVct, "given_name", "family_name")]);

        result.Satisfiable.Should().BeTrue();
        result.PerQuery.Single().Candidates.Should().HaveCount(2);
    }

    // ── builders ──────────────────────────────────────────────────────────────

    private static CachedCredential Cred(string vct, params string[] claims) => new()
    {
        Id = Guid.NewGuid(),
        Vct = vct,
        RawSdJwt = "header.body.sig~",
        AvailableClaimNames = claims,
    };

    private static DcqlCredentialQuery CQ(string id, string vct, params string[] claims) => new()
    {
        Id = id,
        Format = DcqlFormats.SdJwtVc,
        Meta = new DcqlCredentialMeta { VctValues = [vct] },
        Claims = claims.Length == 0 ? null : claims.Select(c => new DcqlClaimQuery { Path = [c] }).ToList(),
    };

    private static DcqlQuery Query(params DcqlCredentialQuery[] credentials) => new() { Credentials = credentials };

    private static DcqlCredentialSetQuery Set(bool required, params string[][] options) => new()
    {
        Options = options.Select(o => (IReadOnlyList<string>)o).ToList(),
        Required = required,
    };

    private static ParsedPresentationRequest Req(DcqlQuery query)
    {
        var (required, optional) = DcqlRequestParser.SplitClaims(query.Credentials[0]);
        return new ParsedPresentationRequest
        {
            ClientId = "did:sorcha:org:verifier",
            ResponseUri = "https://verifier.test/cb",
            Nonce = "nonce-1",
            State = "state-1",
            Query = query,
            RequiredVct = query.Credentials[0].Meta.VctValues![0],
            RequiredClaims = required,
            OptionalClaims = optional,
        };
    }
}
