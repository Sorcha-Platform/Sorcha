// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using Bunit;
using FluentAssertions;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Credentials;
using Sorcha.UI.Core.Models.Credentials;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Components.Credentials;

public class CredentialCardTests : BunitContext
{
    public CredentialCardTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CredentialCardViewModel Card() => new()
    {
        CredentialId = "urn:credential:1",
        Type = "AssuredIdentityCredential",
        DisplayName = "Assured Identity",
        IssuerName = "Acme Identity Assurance Services",
        Status = CredentialStatus.Active,
        IssuedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        ClaimSummary = "Address, email",
        HighlightClaims = new() { ["email"] = "stuart@stuartfraser.net" },
        DisclosableClaims = ["address"]
    };

    [Fact]
    public void CredentialCard_AtRest_ShowsNoClaimValues()
    {
        var cut = Render<CredentialCard>(p => p.Add(c => c.Credential, Card()));

        // The whole point: a list must not print the holder's personal data.
        cut.Markup.Should().NotContain("stuart@stuartfraser.net");
    }

    [Fact]
    public void CredentialCard_AtRest_ShowsTheClaimNameSummary()
    {
        var cut = Render<CredentialCard>(p => p.Add(c => c.Credential, Card()));

        cut.Markup.Should().Contain("Address, email");
    }

    [Fact]
    public void CredentialCard_UsesTheHumanisedName()
    {
        var cut = Render<CredentialCard>(p => p.Add(c => c.Credential, Card()));

        cut.Markup.Should().Contain("Assured Identity");
        cut.Markup.Should().NotContain("AssuredIdentityCredential");
    }

    [Fact]
    public void CredentialAcceptCard_LocksReflectDisclosability()
    {
        var vm = Card();
        vm.HighlightClaims = new() { ["address"] = "Edinburgh", ["email"] = "a@b.c" };
        vm.DisclosableClaims = ["address"];   // email always travels

        var cut = Render<CredentialAcceptCard>(p => p.Add(c => c.Credential, vm));

        // Scope the assertion to each claim row so an inverted implementation
        // (address locked, email open) cannot pass — the padlock must sit next
        // to the claim name it actually governs, not just appear somewhere on
        // the page.
        var openLock = cut.Find("span[aria-label='You control disclosure']");
        var closedLock = cut.Find("span[aria-label='Always disclosed']");

        openLock.NextElementSibling!.TextContent.Should().Be("address");
        closedLock.NextElementSibling!.TextContent.Should().Be("email");
    }
}
