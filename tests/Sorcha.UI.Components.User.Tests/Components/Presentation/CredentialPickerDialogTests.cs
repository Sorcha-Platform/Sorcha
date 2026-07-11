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
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Components.Presentation;

/// <summary>
/// Feature 181 US2 (T026) — the picker's alternative-choice mode: a DCQL credential_set with several
/// satisfiable options presents every option (no auto-pick), and the candidate mode still lists every
/// matching credential.
/// </summary>
public sealed class CredentialPickerDialogTests : BunitContext
{
    public CredentialPickerDialogTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CachedCredential Credential(string vct) => new()
    {
        Id = Guid.NewGuid(),
        Vct = vct,
        RawSdJwt = "jwt~d~",
        AvailableClaimNames = ["givenName"],
    };

    private static CredentialMatch Match(string vct) => new()
    {
        Credential = Credential(vct),
        SatisfiedRequired = ["givenName"],
        AvailableOptional = [],
    };

    private static DcqlQueryMatch QueryMatch(string id, string vct) => new()
    {
        QueryId = id,
        Vct = vct,
        RequiredClaims = ["givenName"],
        OptionalClaims = [],
        Candidates = [Match(vct)],
    };

    [Fact]
    public void AlternativeMode_RendersEverySatisfiableOption_NoAutoPick()
    {
        var setChoice = new DcqlSetChoice
        {
            Options = [["passport"], ["licence"]],
            SatisfiableOptions = [["passport"], ["licence"]],
            Required = true,
            Purpose = "Prove your identity",
        };
        var perQuery = new List<DcqlQueryMatch>
        {
            QueryMatch("passport", "PassportCredential"),
            QueryMatch("licence", "DrivingLicenceCredential"),
        };
        var chosen = new List<IReadOnlyList<string>>();

        var cut = Render<CredentialPickerDialog>(ps => ps
            .Add(p => p.SetChoice, setChoice)
            .Add(p => p.PerQuery, perQuery)
            .Add(p => p.OnOptionChosen, (IReadOnlyList<string> o) => chosen.Add(o)));

        // Both options offered; the citizen has not been auto-committed to one.
        cut.FindAll("[data-testid='credential-picker-option']").Should().HaveCount(2);
        chosen.Should().BeEmpty("the dialog must never auto-pick an alternative");
        cut.Markup.Should().Contain("PassportCredential").And.Contain("DrivingLicenceCredential");
    }

    [Fact]
    public void CandidateMode_ListsEveryMatchingCredential()
    {
        var matches = new List<CredentialMatch> { Match("IdentityCredential"), Match("IdentityCredential") };

        var cut = Render<CredentialPickerDialog>(ps => ps
            .Add(p => p.Matches, matches));

        cut.FindAll("[data-testid='credential-picker-candidate']").Should().HaveCount(2);
        cut.FindAll("[data-testid='credential-picker-option']").Should().BeEmpty();
    }
}
