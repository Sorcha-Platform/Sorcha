// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Presentation;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.Verifier.Engine.Dcql;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Components.Presentation;

/// <summary>
/// Feature 181 US2 (T025) — bUnit coverage for the multi-credential consent surface: one section per
/// credential query (FR-006a), per-query claim approval with required locked / optional toggleable
/// (FR-006b), unsatisfiable required asks flagged with the confirm CTA disabled so there is no partial
/// submit (FR-006d).
/// </summary>
public sealed class ConsentSheetTests : BunitContext
{
    public ConsentSheetTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CachedCredential Credential(string vct, params string[] claims) => new()
    {
        Id = Guid.NewGuid(),
        Vct = vct,
        RawSdJwt = "jwt~d~",
        AvailableClaimNames = claims,
    };

    private static CredentialMatch Match(string vct, string[] required, string[] optional) => new()
    {
        Credential = Credential(vct, required.Concat(optional).ToArray()),
        SatisfiedRequired = required,
        AvailableOptional = optional,
    };

    private static DcqlQueryMatch QueryMatch(string id, string vct, string[] required, string[] optional, bool satisfiable) => new()
    {
        QueryId = id,
        Vct = vct,
        RequiredClaims = required,
        OptionalClaims = optional,
        Candidates = satisfiable ? [Match(vct, required, optional)] : [],
    };

    private static ParsedPresentationRequest Request() => new()
    {
        ClientId = "did:sorcha:org:council",
        ResponseUri = "https://verify.example/direct-post",
        Nonce = "n-1",
        Query = new DcqlQuery { Credentials = [] },
        RequiredVct = "IdentityCredential",
        RequiredClaims = [],
        OptionalClaims = [],
        Purpose = "Prove your eligibility",
    };

    [Fact]
    public void MultiQuery_RendersOneSectionPerQuery()
    {
        var perQuery = new List<DcqlQueryMatch>
        {
            QueryMatch("identity", "IdentityCredential", ["givenName"], [], satisfiable: true),
            QueryMatch("address", "ProofOfAddressCredential", ["postcode"], [], satisfiable: true),
        };
        var result = new DcqlMatchResult
        {
            Satisfiable = true,
            PerQuery = perQuery,
            UnsatisfiedRequiredQueryIds = [],
            SetChoices = [],
        };
        var selections = new Dictionary<string, CredentialMatch>
        {
            ["identity"] = perQuery[0].Candidates[0],
            ["address"] = perQuery[1].Candidates[0],
        };

        var cut = Render<ConsentSheet>(ps => ps
            .Add(p => p.Request, Request())
            .Add(p => p.Result, result)
            .Add(p => p.Selections, selections)
            .Add(p => p.QueryOptionalToggles, new Dictionary<string, Dictionary<string, bool>>()));

        cut.FindAll("[data-testid='consent-query-section']").Should().HaveCount(2);
        cut.FindAll("[data-testid='consent-unsatisfiable']").Should().BeEmpty();
        cut.Find("[data-testid='consent-confirm']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void MultiQuery_UnsatisfiableRequired_FlagsSectionAndDisablesConfirm()
    {
        var perQuery = new List<DcqlQueryMatch>
        {
            QueryMatch("identity", "IdentityCredential", ["givenName"], [], satisfiable: true),
            QueryMatch("address", "ProofOfAddressCredential", ["postcode"], [], satisfiable: false),
        };
        var result = new DcqlMatchResult
        {
            Satisfiable = false,
            PerQuery = perQuery,
            UnsatisfiedRequiredQueryIds = ["address"],
            SetChoices = [],
        };
        var selections = new Dictionary<string, CredentialMatch>
        {
            ["identity"] = perQuery[0].Candidates[0],
        };

        var cut = Render<ConsentSheet>(ps => ps
            .Add(p => p.Request, Request())
            .Add(p => p.Result, result)
            .Add(p => p.Selections, selections)
            .Add(p => p.QueryOptionalToggles, new Dictionary<string, Dictionary<string, bool>>()));

        // The unsatisfiable required ask is flagged, and no partial submit is possible.
        var flag = cut.Find("[data-testid='consent-unsatisfiable']");
        flag.Should().NotBeNull();
        cut.Find("[data-testid='consent-confirm']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void MultiQuery_RequiredClaimsLocked_OptionalToggleable()
    {
        var perQuery = new List<DcqlQueryMatch>
        {
            QueryMatch("identity", "IdentityCredential", ["givenName"], ["dateOfBirth"], satisfiable: true),
        };
        var result = new DcqlMatchResult
        {
            Satisfiable = true,
            PerQuery = perQuery,
            UnsatisfiedRequiredQueryIds = [],
            SetChoices = [],
        };
        var selections = new Dictionary<string, CredentialMatch> { ["identity"] = perQuery[0].Candidates[0] };

        var cut = Render<ConsentSheet>(ps => ps
            .Add(p => p.Request, Request())
            .Add(p => p.Result, result)
            .Add(p => p.Selections, selections)
            .Add(p => p.QueryOptionalToggles, new Dictionary<string, Dictionary<string, bool>>()));

        cut.FindAll("[data-testid='consent-required-claim']").Should().ContainSingle();
        cut.FindAll("[data-testid='consent-optional-claim']").Should().ContainSingle();
    }

    [Fact]
    public void MultiQuery_Confirm_InvokesCallback()
    {
        var perQuery = new List<DcqlQueryMatch>
        {
            QueryMatch("identity", "IdentityCredential", ["givenName"], [], satisfiable: true),
        };
        var result = new DcqlMatchResult
        {
            Satisfiable = true,
            PerQuery = perQuery,
            UnsatisfiedRequiredQueryIds = [],
            SetChoices = [],
        };
        var selections = new Dictionary<string, CredentialMatch> { ["identity"] = perQuery[0].Candidates[0] };
        var confirmed = false;

        var cut = Render<ConsentSheet>(ps => ps
            .Add(p => p.Request, Request())
            .Add(p => p.Result, result)
            .Add(p => p.Selections, selections)
            .Add(p => p.QueryOptionalToggles, new Dictionary<string, Dictionary<string, bool>>())
            .Add(p => p.OnConfirm, () => confirmed = true));

        cut.Find("[data-testid='consent-confirm']").Click();

        confirmed.Should().BeTrue();
    }
}
