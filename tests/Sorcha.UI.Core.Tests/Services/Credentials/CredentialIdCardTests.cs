// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
}
