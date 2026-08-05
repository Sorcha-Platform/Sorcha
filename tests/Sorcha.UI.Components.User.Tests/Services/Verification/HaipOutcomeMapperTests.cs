// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Engine.Models;
using Xunit;
using Sorcha.Verification.Abstractions;

namespace Sorcha.UI.Components.User.Tests.Services.Verification;

public class HaipOutcomeMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_AcceptedResult_ProducesPassOutcomeWithThreeLayers()
    {
        var claims = new Dictionary<string, object?> { ["fullName"] = "Stuart Fraser", ["age_over_18"] = true };

        var outcome = HaipOutcomeMapper.Map(
            accepted: true, disclosedClaims: claims, errors: [], holderKeyVerified: true,
            vpToken: null, completedAt: At);

        outcome.Accepted.Should().BeTrue();
        outcome.DisclosedClaims.Should().ContainKey("fullName");
        outcome.IssuerSignature.Should().Be(IssuerSignatureStatus.Verified);
        outcome.Layers.Should().HaveCount(3);
        outcome.Layers.Should().OnlyContain(l => l.Status == VerificationStatus.Verified);
        outcome.Layers.Select(l => l.Layer).Should().BeEquivalentTo(new[]
        {
            ValidationLayer.LivePresentation, ValidationLayer.IssuerSignature, ValidationLayer.Revocation
        });
    }

    [Fact]
    public void Map_RejectedResult_ProducesFailOutcomeAndCarriesErrors()
    {
        var outcome = HaipOutcomeMapper.Map(
            accepted: false, disclosedClaims: new Dictionary<string, object?>(),
            errors: ["nonce mismatch"], holderKeyVerified: false, vpToken: null, completedAt: At);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain("nonce mismatch");
        outcome.Layers.First(l => l.Layer == ValidationLayer.LivePresentation).Status
            .Should().Be(VerificationStatus.Failed);
    }

    [Fact]
    public void Map_ParsesIssuerAndJtiFromVpToken_ForTheTrailAndAnchorLookup()
    {
        // header {"alg":"EdDSA"} . payload {"iss":"did:sorcha:org:ws1qabc","jti":"cred-123"} . sig
        const string header = "eyJhbGciOiJFZERTQSJ9";
        const string payload = "eyJpc3MiOiJkaWQ6c29yY2hhOm9yZzp3czFxYWJjIiwianRpIjoiY3JlZC0xMjMifQ";
        var vp = $"{header}.{payload}.sig~";

        var outcome = HaipOutcomeMapper.Map(
            accepted: true, disclosedClaims: new Dictionary<string, object?>(),
            errors: [], holderKeyVerified: true, vpToken: vp, completedAt: At);

        var issuerLayer = outcome.Layers.First(l => l.Layer == ValidationLayer.IssuerSignature);
        issuerLayer.Detail.Should().ContainKey("iss").WhoseValue.Should().Be("did:sorcha:org:ws1qabc");
        issuerLayer.Detail.Should().ContainKey("jti").WhoseValue.Should().Be("cred-123");
    }
}
