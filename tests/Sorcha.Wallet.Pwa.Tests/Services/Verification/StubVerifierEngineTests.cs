// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Wallet.Pwa.Services.Verification;
using LibVerifyOutcome = Sorcha.UI.Components.User.Models.Verification.VerifyOutcome;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Verification;

/// <summary>
/// Tests for <see cref="StubVerifierEngine"/> (Feature 125, PR-C). The
/// stub is the v1 verifier wired behind <c>IVerifierEngine</c> — it parses
/// a minimal demo-offer JSON envelope so the UI flow can be exercised
/// before the real validator extraction lands.
/// </summary>
public sealed class StubVerifierEngineTests
{
    private static readonly StubVerifierEngine Sut = new(NullLogger<StubVerifierEngine>.Instance);

    private static VerifierEngineRequest Request(string offer)
        => new(offer, VerifierClientId: "test-client", Nonce: "test-nonce");

    [Fact]
    public async Task VerifyAsync_PassOffer_ReturnsPass_PopulatesFields()
    {
        var json = """{"outcome":"pass","holderDisplayName":"Liam Buchanan","issuerOrgName":"Caledonian Water","credentialType":"WaterEngineerCredential/v1","claims":{"givenName":"Liam"}}""";

        var result = await Sut.VerifyAsync(Request(json));

        result.Outcome.Should().Be(LibVerifyOutcome.Pass);
        result.HolderDisplayName.Should().Be("Liam Buchanan");
        result.IssuerOrgName.Should().Be("Caledonian Water");
        result.CredentialType.Should().Be("WaterEngineerCredential/v1");
        result.DisclosedClaims.Should().ContainKey("givenName");
        result.Messages.Should().BeEmpty("a clean pass produces no diagnostic messages.");
    }

    [Fact]
    public async Task VerifyAsync_WarnOffer_AddsDefaultMessage()
    {
        var json = """{"outcome":"warn","holderDisplayName":"Liam","issuerOrgName":"Caledonian","credentialType":"x/v1"}""";

        var result = await Sut.VerifyAsync(Request(json));

        result.Outcome.Should().Be(LibVerifyOutcome.Warn);
        result.Messages.Should().NotBeEmpty("the warn path emits a plain-English diagnostic by default.");
    }

    [Fact]
    public async Task VerifyAsync_FailOffer_AddsRevocationGuidance()
    {
        var json = """{"outcome":"fail","holderDisplayName":"X","issuerOrgName":"Y","credentialType":"z/v1"}""";

        var result = await Sut.VerifyAsync(Request(json));

        result.Outcome.Should().Be(LibVerifyOutcome.Fail);
        result.Messages.Should().NotBeEmpty();
        result.Messages[0].Should().ContainAny("Do not", "could not", "verify");
    }

    [Fact]
    public async Task VerifyAsync_MalformedOffer_ReturnsFail_WithUserSafeMessage()
    {
        var result = await Sut.VerifyAsync(Request("not json at all <<<"));

        result.Outcome.Should().Be(LibVerifyOutcome.Fail);
        result.Messages.Should().ContainSingle(m => m.Contains("Couldn't read", System.StringComparison.OrdinalIgnoreCase));
        result.HolderDisplayName.Should().Be("Unknown holder");
    }

    [Fact]
    public async Task VerifyAsync_EmptyJsonObject_DefaultsToPassWithUnknownFields()
    {
        var result = await Sut.VerifyAsync(Request("{}"));

        result.Outcome.Should().Be(LibVerifyOutcome.Pass);
        result.HolderDisplayName.Should().Be("Unknown holder");
        result.IssuerOrgName.Should().Be("Unknown issuer");
        result.CredentialType.Should().Be("Unknown credential");
    }

    [Fact]
    public async Task VerifyAsync_PopulatesTrustPanelJson()
    {
        var json = """{"outcome":"pass","holderDisplayName":"Liam","issuerOrgName":"CW","credentialType":"WE/v1"}""";
        var result = await Sut.VerifyAsync(Request(json));

        result.TrustPanelJson.Should().NotBeNullOrEmpty();
        result.TrustPanelJson.Should().Contain("Liam");
        result.TrustPanelJson.Should().Contain("WE/v1");
    }
}
