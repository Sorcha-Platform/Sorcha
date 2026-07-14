// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Credentials;

/// <summary>
/// Tests for <see cref="CredentialIdCard"/> — the held-credential → id-card adapter that renders
/// identity credentials as the styled ID card rather than a raw claims table.
/// </summary>
public class CredentialIdCardTests
{
    [Theory]
    [InlineData("AssuredIdentityCredential", true)]
    [InlineData("digitalIdentity", true)]
    [InlineData("MembershipCredential", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsIdentityCredential_DetectsByTypeName(string? type, bool expected)
        => CredentialIdCard.IsIdentityCredential(type).Should().Be(expected);

    [Fact]
    public void BuildConfig_MapsClaims_Portrait_AndIssuedState()
    {
        var cred = new CredentialDetailViewModel
        {
            Type = "AssuredIdentityCredential",
            IssuerDid = "did:sorcha:org:ws1abc",
            Claims = new Dictionary<string, object>
            {
                ["fullName"] = "Stuart Fraser",
                ["dateOfBirth"] = "1968-08-19",
                ["email"] = "stuart@example.net",
                ["portrait"] = "BASE64IMG",
            },
        };

        var cfg = CredentialIdCard.BuildConfig(cred);

        cfg.FieldValues["/fullName"].Should().Be("Stuart Fraser");
        cfg.FieldValues["/dateOfBirth"].Should().Be("1968-08-19");
        cfg.FieldValues["/email"].Should().Be("stuart@example.net");
        // portrait maps to the sibling pointer IdCardLayout scans for the card photo
        cfg.FieldValues["/portrait/tokenImageBase64"].Should().Be("BASE64IMG");
        cfg.Watermark.Should().Be(IdCardWatermark.Issued);
        cfg.Editable.Should().BeFalse();
        cfg.CredentialName.Should().Be("Assured Identity");
    }

    [Fact]
    public void BuildConfig_SynthesisesFullNameFromGivenAndFamily()
    {
        var cred = new CredentialDetailViewModel
        {
            Type = "AssuredIdentityCredential",
            Claims = new Dictionary<string, object> { ["givenName"] = "Stuart", ["familyName"] = "Fraser" },
        };

        var cfg = CredentialIdCard.BuildConfig(cred);

        cfg.FieldValues["/fullName"].Should().Be("Stuart Fraser");
    }

    [Fact]
    public void BuildConfig_IssuerName_FlowsToCardHeader()
    {
        var cred = new CredentialDetailViewModel
        {
            Type = "AssuredIdentityCredential",
            IssuerName = "Acme Identity Assurance Services",
            Claims = new Dictionary<string, object> { ["fullName"] = "Stuart Fraser" },
        };

        var cfg = CredentialIdCard.BuildConfig(cred);

        cfg.IssuerName.Should().Be("Acme Identity Assurance Services");
        cfg.CredentialName.Should().Be("Assured Identity");
    }

    [Fact]
    public void BuildConfig_ModelAgnostic_MatchesModelOverload_ForWebPwaParity()
    {
        // The web (CredentialDetailViewModel) and the PWA (CachedCredential) feed the SAME
        // model-agnostic core, so the rendered card is identical. Guard that parity.
        var cred = new CredentialDetailViewModel
        {
            Type = "AssuredIdentityCredential",
            IssuerName = "AIAS",
            Claims = new Dictionary<string, object>
            {
                ["fullName"] = "Stuart Fraser",
                ["dateOfBirth"] = "1968-08-19",
                ["portrait"] = "IMG",
            },
        };
        var claims = new Dictionary<string, string?>
        {
            ["fullName"] = "Stuart Fraser",
            ["dateOfBirth"] = "1968-08-19",
            ["portrait"] = "IMG",
        };

        var fromModel = CredentialIdCard.BuildConfig(cred);
        var fromPrimitives = CredentialIdCard.BuildConfig("AssuredIdentityCredential", "Assured Identity", "AIAS", claims);

        fromPrimitives.IssuerName.Should().Be(fromModel.IssuerName);
        fromPrimitives.CredentialName.Should().Be(fromModel.CredentialName);
        fromPrimitives.FieldValues["/fullName"].Should().Be(fromModel.FieldValues["/fullName"]);
        fromPrimitives.FieldValues["/portrait/tokenImageBase64"].Should().Be(fromModel.FieldValues["/portrait/tokenImageBase64"]);
    }

    [Fact]
    public void BuildConfig_PoisonDigestArrayClaim_NeverRendersRawJson()
    {
        // Third door onto the n1 leak. CredentialIdCard.BuildConfig used to read the RAW
        // Claims dictionary and call kv.Value?.ToString() -- for a boxed JsonElement of
        // ValueKind.Object that returns GetRawText(), i.e. the exact {"_sd":[...]} blob that
        // shipped to a citizen's phone. This is the worst of the three doors: the ID-card face
        // is the surface the bug report actually hit (AssuredIdentityCredential). Deserializing
        // real JSON (rather than hand-building a fake) mirrors how
        // CredentialApiService.MapToDetailViewModel actually populates Claims in production.
        var claimsJson = "{\"address\":{\"_sd\":[\"zSH_kfTeW2Mlc\"]},\"fullName\":\"Stuart Fraser\"}";
        var claims = JsonSerializer.Deserialize<Dictionary<string, object>>(claimsJson)!;

        var cred = new CredentialDetailViewModel
        {
            Type = "AssuredIdentityCredential",
            Claims = claims,
        };

        var cfg = CredentialIdCard.BuildConfig(cred);

        var addressValue = cfg.FieldValues.TryGetValue("/address", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        addressValue.Should().NotContain("_sd");
        addressValue.Should().NotContain("{");
        cfg.FieldValues["/fullName"].Should().Be("Stuart Fraser");
    }

    [Fact]
    public void BuildConfig_LegitimateNestedAddressClaim_RendersStructurally()
    {
        // SC-1 also forbids the opposite failure mode: blanking every object claim. A genuine
        // nested claim like address.town must still render something meaningful to a citizen.
        var claimsJson = "{\"address\":{\"town\":\"Edinburgh\",\"line1\":\"1 High Street\"},\"fullName\":\"Stuart Fraser\"}";
        var claims = JsonSerializer.Deserialize<Dictionary<string, object>>(claimsJson)!;

        var cred = new CredentialDetailViewModel
        {
            Type = "AssuredIdentityCredential",
            Claims = claims,
        };

        var cfg = CredentialIdCard.BuildConfig(cred);

        cfg.FieldValues.Should().ContainKey("/address");
        var addressValue = cfg.FieldValues["/address"]?.ToString() ?? string.Empty;
        addressValue.Should().NotContain("{");
        addressValue.Should().Contain("Edinburgh");
    }
}
