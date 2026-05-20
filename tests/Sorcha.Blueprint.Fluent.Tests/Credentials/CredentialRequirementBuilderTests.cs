// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Fluent.Tests.Credentials;

public class CredentialRequirementBuilderTests
{
    [Fact]
    public void Build_WithType_SetsType()
    {
        var builder = new CredentialRequirementBuilder();
        builder.OfType("LicenseCredential");
        var req = builder.Build();

        req.Type.Should().Be("LicenseCredential");
    }

    [Fact]
    public void Build_WithIssuer_SetsDidAllowlistTrustPolicy()
    {
        var builder = new CredentialRequirementBuilder();
        builder.OfType("LicenseCredential")
            .FromIssuer("did:sorcha:issuer:gov");
        var req = builder.Build();

        req.TrustPolicy.AllowedIssuerDids().Should().ContainSingle().Which.Should().Be("did:sorcha:issuer:gov");
    }

    [Fact]
    public void Build_WithMultipleIssuers_AddsAll()
    {
        var builder = new CredentialRequirementBuilder();
        builder.OfType("LicenseCredential")
            .FromIssuer("did:sorcha:issuer:gov1")
            .FromIssuer("did:sorcha:issuer:gov2");
        var req = builder.Build();

        req.TrustPolicy.AllowedIssuerDids().Should().HaveCount(2);
    }

    [Fact]
    public void Build_RequireClaimNameOnly_SetsConstraintWithNullValue()
    {
        var builder = new CredentialRequirementBuilder();
        builder.OfType("LicenseCredential")
            .RequireClaim("licenseType");
        var req = builder.Build();

        req.RequiredClaims.Should().ContainSingle();
        var claim = req.RequiredClaims!.First();
        claim.ClaimName.Should().Be("licenseType");
        claim.ExpectedValue.Should().BeNull();
    }

    [Fact]
    public void Build_RequireClaimWithValue_SetsConstraintWithExpectedValue()
    {
        var builder = new CredentialRequirementBuilder();
        builder.OfType("LicenseCredential")
            .RequireClaim("licenseType", "A");
        var req = builder.Build();

        var claim = req.RequiredClaims!.First();
        claim.ClaimName.Should().Be("licenseType");
        claim.ExpectedValue.Should().Be("A");
    }

    [Fact]
    public void Build_WithRevocationCheck_SetsPolicy()
    {
        var builder = new CredentialRequirementBuilder();
        builder.OfType("LicenseCredential")
            .WithRevocationCheck(RevocationCheckPolicy.FailOpen);
        var req = builder.Build();

        req.RevocationCheckPolicy.Should().Be(RevocationCheckPolicy.FailOpen);
    }

    [Fact]
    public void Build_WithDescription_SetsDescription()
    {
        var builder = new CredentialRequirementBuilder();
        builder.OfType("LicenseCredential")
            .WithDescription("Valid driving license required");
        var req = builder.Build();

        req.Description.Should().Be("Valid driving license required");
    }

    [Fact]
    public void Build_FullChain_SetsAllProperties()
    {
        var builder = new CredentialRequirementBuilder();
        builder
            .OfType("LicenseCredential")
            .FromIssuer("did:sorcha:issuer:gov")
            .RequireClaim("licenseType", "A")
            .RequireClaim("issuedCountry")
            .WithRevocationCheck(RevocationCheckPolicy.FailClosed)
            .WithDescription("Class A driving license");
        var req = builder.Build();

        req.Type.Should().Be("LicenseCredential");
        req.TrustPolicy.AllowedIssuerDids().Should().ContainSingle().Which.Should().Be("did:sorcha:issuer:gov");
        req.RequiredClaims.Should().HaveCount(2);
        req.RevocationCheckPolicy.Should().Be(RevocationCheckPolicy.FailClosed);
        req.Description.Should().Be("Class A driving license");
    }
}
