// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Fluent;

namespace Sorcha.Blueprint.Fluent.Tests.Credentials;

/// <summary>
/// Unit tests for CredentialIssuanceBuilder fluent API.
/// </summary>
public class CredentialIssuanceBuilderTests
{
    [Fact]
    public void OfType_SetsCredentialType()
    {
        var builder = new CredentialIssuanceBuilder();
        builder.OfType("LicenseCredential");

        var config = builder.Build();

        config.CredentialType.Should().Be("LicenseCredential");
    }

    [Fact]
    public void MapClaim_AddsClaimMapping()
    {
        var builder = new CredentialIssuanceBuilder();
        builder.MapClaim("license_type", "/licenseType");

        var config = builder.Build();

        config.ClaimMappings.Should().HaveCount(1);
        config.ClaimMappings!.First().ClaimName.Should().Be("license_type");
        config.ClaimMappings.First().SourceField.Should().Be("/licenseType");
    }

    [Fact]
    public void MapClaim_MultipleMappings_AddsAll()
    {
        var builder = new CredentialIssuanceBuilder();
        builder
            .MapClaim("name", "/holderName")
            .MapClaim("type", "/licenseType")
            .MapClaim("issued", "/issueDate");

        var config = builder.Build();

        config.ClaimMappings.Should().HaveCount(3);
    }

    [Fact]
    public void ToRecipient_SetsRecipientParticipantId()
    {
        var builder = new CredentialIssuanceBuilder();
        builder.ToRecipient("applicant");

        var config = builder.Build();

        config.RecipientParticipantId.Should().Be("applicant");
    }

    [Fact]
    public void ExpiresAfter_SetsExpiryDuration()
    {
        var builder = new CredentialIssuanceBuilder();
        builder.ExpiresAfter("P365D");

        var config = builder.Build();

        config.ExpiryDuration.Should().Be("P365D");
    }

    [Fact]
    public void RecordOnRegister_SetsRegisterId()
    {
        var builder = new CredentialIssuanceBuilder();
        builder.RecordOnRegister("register-001");

        var config = builder.Build();

        config.RegisterId.Should().Be("register-001");
    }

    [Fact]
    public void MakeDisclosable_AddsClaimToDisclosableList()
    {
        var builder = new CredentialIssuanceBuilder();
        builder
            .MakeDisclosable("name")
            .MakeDisclosable("address");

        var config = builder.Build();

        config.Disclosable.Should().HaveCount(2);
        config.Disclosable.Should().Contain("name");
        config.Disclosable.Should().Contain("address");
    }

    [Fact]
    public void FluentChaining_BuildsCompleteConfig()
    {
        var builder = new CredentialIssuanceBuilder();
        builder
            .OfType("LicenseCredential")
            .MapClaim("license_type", "/licenseType")
            .MapClaim("holder_name", "/holderName")
            .ToRecipient("applicant")
            .ExpiresAfter("P365D")
            .RecordOnRegister("license-register")
            .MakeDisclosable("holder_name");

        var config = builder.Build();

        config.CredentialType.Should().Be("LicenseCredential");
        config.ClaimMappings.Should().HaveCount(2);
        config.RecipientParticipantId.Should().Be("applicant");
        config.ExpiryDuration.Should().Be("P365D");
        config.RegisterId.Should().Be("license-register");
        config.Disclosable.Should().ContainSingle().Which.Should().Be("holder_name");
    }

    [Fact]
    public void Build_WithNoMappings_ReturnsNullOrEmptyClaimMappings()
    {
        var builder = new CredentialIssuanceBuilder();
        builder.OfType("TestCredential");

        var config = builder.Build();

        // Builder doesn't add ClaimMappings if none were mapped
        (config.ClaimMappings == null || !config.ClaimMappings.Any()).Should().BeTrue();
    }

    [Fact]
    public void Build_WithNoDisclosable_ReturnsNullDisclosable()
    {
        var builder = new CredentialIssuanceBuilder();
        builder.OfType("TestCredential");

        var config = builder.Build();

        config.Disclosable.Should().BeNull();
    }

    [Fact]
    public void MakeDisclosablePath_AddsJsonPointerToConfig()
    {
        var builder = new CredentialIssuanceBuilder();
        builder
            .OfType("AddressCredential")
            .MakeDisclosablePath("/address/locality")
            .MakeDisclosablePath("/address/country");

        var config = builder.Build();

        config.Disclosable.Should().HaveCount(2);
        config.Disclosable.Should().Contain("/address/locality");
        config.Disclosable.Should().Contain("/address/country");
    }

    [Fact]
    public void MakeDisclosable_AndMakeDisclosablePath_CoexistInSameConfig()
    {
        var builder = new CredentialIssuanceBuilder();
        builder
            .OfType("MixedCredential")
            .MakeDisclosable("name")
            .MakeDisclosablePath("/address/locality")
            .MakeDisclosable("age");

        var config = builder.Build();

        config.Disclosable.Should().HaveCount(3);
        config.Disclosable.Should().Contain("name");
        config.Disclosable.Should().Contain("age");
        config.Disclosable.Should().Contain("/address/locality");
    }

    [Fact]
    public void MakeDisclosablePath_InvalidPath_ThrowsArgumentException()
    {
        var builder = new CredentialIssuanceBuilder();

        var act = () => builder.MakeDisclosablePath("address/locality"); // missing leading /

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must start with '/'*");
    }
}
