// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using FluentAssertions;
using Sorcha.UI.Core.Components.Wallet;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CredentialDisplay"/> — the card text derivation.
/// These guard the "card reads human, not machine" requirement: a citizen
/// never sees a raw DID or a truncated VCT chip like "ASSURE".
/// </summary>
public sealed class CredentialDisplayTests
{
    private static CachedCredential Cred(
        string vct = "AssuredIdentityCredential",
        string? issuerDid = null,
        string? displayLabel = null) => new()
    {
        Id = Guid.NewGuid(),
        Vct = vct,
        RawSdJwt = "eyJ.fake.jwt",
        AvailableClaimNames = new List<string>(),
        IssuerDid = issuerDid,
        DisplayLabel = displayLabel,
    };

    [Theory]
    [InlineData("AssuredIdentityCredential", "Assured Identity")]
    [InlineData("DrivingLicenceCredential", "Driving Licence")]
    [InlineData("https://example.org/vc/AssuredIdentityCredential", "Assured Identity")]
    [InlineData("Credential", "Credential")]
    [InlineData("", "Credential")]
    [InlineData(null, "Credential")]
    public void Humanize_SplitsPascalCase_AndDropsTrailingCredential(string? vct, string expected)
    {
        CredentialDisplay.Humanize(vct).Should().Be(expected);
    }

    [Fact]
    public void ShortenDid_ShowsFirst8AndLast5OfTail_NeverFullDid()
    {
        CredentialDisplay.ShortenDid("did:sorcha:org:WS11QRNVabcdefghijHGNRK")
            .Should().Be("WS11QRNV…HGNRK");
    }

    [Fact]
    public void ShortenDid_LeavesShortTailUnchanged()
    {
        CredentialDisplay.ShortenDid("did:sorcha:org:acme").Should().Be("acme");
    }

    [Fact]
    public void Issuer_Demo_IsSorchaDev()
    {
        CredentialDisplay.Issuer(Cred(issuerDid: "did:sorcha:org:demo")).Should().Be("sorcha.dev");
    }

    [Fact]
    public void Issuer_RealDid_IsShortenedNotRaw()
    {
        var did = "did:sorcha:org:WS11QRNVabcdefghijHGNRK";
        var issuer = CredentialDisplay.Issuer(Cred(issuerDid: did));
        issuer.Should().NotBe(did);
        issuer.Should().Be("WS11QRNV…HGNRK");
    }

    [Fact]
    public void Issuer_NoDid_FallsBackToSorcha()
    {
        CredentialDisplay.Issuer(Cred(issuerDid: null)).Should().Be("Sorcha");
    }

    [Fact]
    public void Name_PrefersDisplayLabel_ElseHumanizedVct()
    {
        CredentialDisplay.Name(Cred(displayLabel: "My ID card")).Should().Be("My ID card");
        CredentialDisplay.Name(Cred(vct: "AssuredIdentityCredential", displayLabel: null))
            .Should().Be("Assured Identity");
    }

    [Fact]
    public void TypeChip_MapsKnownVcts_AndHidesUnknown()
    {
        CredentialDisplay.TypeChip(Cred(issuerDid: "did:sorcha:org:demo")).Should().Be("EU-ID");
        CredentialDisplay.TypeChip(Cred(vct: "AssuredIdentityCredential")).Should().Be("ID");
        CredentialDisplay.TypeChip(Cred(vct: "DrivingLicenceCredential")).Should().Be("DL");
        // Unknown type → no chip (a wrong-looking chip is worse than none).
        CredentialDisplay.TypeChip(Cred(vct: "SomeObscureThing")).Should().BeNull();
    }

    [Fact]
    public void ExpiryStatus_NoExpiry_IsValid()
    {
        var now = DateTimeOffset.UtcNow;
        CredentialDisplay.ExpiryStatus(null, now).Should().Be((CredentialStatus.Valid, (string?)null));
    }

    [Fact]
    public void ExpiryStatus_FarFuture_IsValid()
    {
        var now = DateTimeOffset.UtcNow;
        CredentialDisplay.ExpiryStatus(now.AddDays(90), now).Status.Should().Be(CredentialStatus.Valid);
    }

    [Fact]
    public void ExpiryStatus_WithinTwoWeeks_IsExpiringSoon_WithText()
    {
        var now = DateTimeOffset.UtcNow;
        var (status, text) = CredentialDisplay.ExpiryStatus(now.AddDays(3), now);
        status.Should().Be(CredentialStatus.ExpiringSoon);
        text.Should().Be("Expires in 3 days");
    }

    [Fact]
    public void ExpiryStatus_Past_IsRevokedExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var (status, text) = CredentialDisplay.ExpiryStatus(now.AddDays(-1), now);
        status.Should().Be(CredentialStatus.Revoked);
        text.Should().Be("Expired");
    }
}
