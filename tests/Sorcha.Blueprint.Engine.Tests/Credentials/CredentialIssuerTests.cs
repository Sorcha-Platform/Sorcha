// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Unit tests for CredentialIssuer — claim mapping, DID URI generation, SD-JWT token creation, expiry calculation.
/// </summary>
public class CredentialIssuerTests
{
    private readonly Mock<ISdJwtService> _sdJwtServiceMock;
    private readonly CredentialIssuer _issuer;

    public CredentialIssuerTests()
    {
        _sdJwtServiceMock = new Mock<ISdJwtService>();
        _issuer = new CredentialIssuer(_sdJwtServiceMock.Object);
    }

    [Fact]
    public async Task IssueAsync_MapsClaims_FromActionData()
    {
        // Arrange
        var config = CreateConfig("LicenseCredential",
            new ClaimMapping { ClaimName = "license_type", SourceField = "/licenseType" },
            new ClaimMapping { ClaimName = "holder_name", SourceField = "/holderName" });

        var actionData = new Dictionary<string, object>
        {
            ["licenseType"] = "ClassA",
            ["holderName"] = "Alice",
            ["extraField"] = "ignored"
        };

        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, actionData, "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.Claims.Should().ContainKey("license_type").WhoseValue.Should().Be("ClassA");
        result.Claims.Should().ContainKey("holder_name").WhoseValue.Should().Be("Alice");
        result.Claims.Should().NotContainKey("extraField");
    }

    [Fact]
    public async Task IssueAsync_AddsStandardClaims_VctOnly_NoTypeClaim()
    {
        // Arrange
        var config = CreateConfig("IdentityAttestation");
        var actionData = new Dictionary<string, object>();
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, actionData, "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        // SD-JWT VC (draft-ietf-oauth-sd-jwt-vc §3.2.2.1): vct is the SOLE type claim.
        result.Claims.Should().ContainKey("vct").WhoseValue.Should().Be("IdentityAttestation");
        result.Claims.Should().NotContainKey("type", "SD-JWT VC has no type claim");
    }

    [Fact]
    public async Task IssueAsync_WithVct_WritesVctClaim_AndNoTypeClaim()
    {
        // Arrange
        var config = CreateConfig("AssuredIdentityCredential");
        config.Vct = "https://sorcha.dev/vc/assured-identity/v1";
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.Claims["vct"].Should().Be("https://sorcha.dev/vc/assured-identity/v1");
        result.Claims.Should().NotContainKey("type", "SD-JWT VC has no type claim");
        result.Type.Should().Be("https://sorcha.dev/vc/assured-identity/v1");
    }

    [Fact]
    public async Task IssueAsync_WithoutVct_FallsBackToCredentialType()
    {
        // Arrange
        var config = CreateConfig("LegacyBareName");
        config.Vct = null;
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.Claims["vct"].Should().Be("LegacyBareName");
        result.Claims.Should().NotContainKey("type");
        result.Type.Should().Be("LegacyBareName");
    }

    [Fact]
    public async Task IssueAsync_WithDisplayName_CarriesCredentialNameInDisplayConfigJson()
    {
        // Arrange
        var config = CreateConfig("AssuredIdentityCredential");
        config.DisplayName = "Assured Identity";
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.DisplayConfigJson.Should().NotBeNull();
        result.DisplayConfigJson.Should().Contain("\"credentialName\":\"Assured Identity\"");
    }

    [Fact]
    public async Task IssueAsync_GeneratesCredentialId_AsUrnUuid()
    {
        // Arrange
        var config = CreateConfig("TestCredential");
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.CredentialId.Should().StartWith("urn:uuid:");
        Guid.TryParse(result.CredentialId["urn:uuid:".Length..], out _).Should().BeTrue();
    }

    [Fact]
    public async Task IssueAsync_SetsIssuerAndSubject()
    {
        // Arrange
        var config = CreateConfig("TestCredential");
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(),
            "did:issuer:authority", "did:recipient:holder",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.IssuerDid.Should().Be("did:issuer:authority");
        result.SubjectDid.Should().Be("did:recipient:holder");
    }

    [Fact]
    public async Task IssueAsync_CalculatesExpiry_FromIsoDuration()
    {
        // Arrange
        var config = CreateConfig("TestCredential");
        config.ExpiryDuration = "P365D";
        SetupSdJwtService();

        var before = DateTimeOffset.UtcNow;

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        var after = DateTimeOffset.UtcNow;

        // Assert
        result.IssuedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        result.ExpiresAt.Should().NotBeNull();
        result.ExpiresAt!.Value.Should().BeCloseTo(before.AddDays(365), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IssueAsync_NoExpiry_WhenDurationNotSet()
    {
        // Arrange
        var config = CreateConfig("TestCredential");
        config.ExpiryDuration = null;
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_InvalidDuration_DefaultsToOneYear()
    {
        // Arrange
        var config = CreateConfig("TestCredential");
        config.ExpiryDuration = "INVALID";
        SetupSdJwtService();

        var before = DateTimeOffset.UtcNow;

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.ExpiresAt.Should().NotBeNull();
        result.ExpiresAt!.Value.Should().BeCloseTo(before.AddDays(365), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IssueAsync_PassesDisclosableClaims_ToSdJwtService()
    {
        // Arrange
        var config = CreateConfig("TestCredential");
        config.Disclosable = ["claim1", "claim2"];
        SetupSdJwtService();

        // Act
        await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        _sdJwtServiceMock.Verify(s => s.CreateTokenAsync(
            It.IsAny<Dictionary<string, object>>(),
            It.Is<List<string>>(d => d.Count == 2 && d.Contains("claim1") && d.Contains("claim2")),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<byte[]>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IssueAsync_ReturnsRawToken_FromSdJwtService()
    {
        // Arrange
        var config = CreateConfig("TestCredential");
        var expectedToken = "eyJ.eyJ.sig~disclosure1~disclosure2~";
        _sdJwtServiceMock
            .Setup(s => s.CreateTokenAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<List<string>?>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtToken { RawToken = expectedToken });

        // Act
        var result = await _issuer.IssueAsync(
            config, new Dictionary<string, object>(), "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.RawToken.Should().Be(expectedToken);
    }

    [Fact]
    public async Task IssueAsync_TrimsLeadingSlash_FromSourceField()
    {
        // Arrange
        var config = CreateConfig("TestCredential",
            new ClaimMapping { ClaimName = "name", SourceField = "/fullName" });

        var actionData = new Dictionary<string, object> { ["fullName"] = "Bob" };
        SetupSdJwtService();

        // Act
        var result = await _issuer.IssueAsync(
            config, actionData, "did:issuer:1", "did:recipient:1",
            new byte[] { 1, 2, 3 }, "EdDSA");

        // Assert
        result.Claims.Should().ContainKey("name").WhoseValue.Should().Be("Bob");
    }

    private static CredentialIssuanceConfig CreateConfig(string type, params ClaimMapping[] mappings)
    {
        return new CredentialIssuanceConfig
        {
            CredentialType = type,
            ClaimMappings = mappings.Length > 0 ? mappings.ToList() : [],
            RecipientParticipantId = "recipient"
        };
    }

    private void SetupSdJwtService()
    {
        _sdJwtServiceMock
            .Setup(s => s.CreateTokenAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<List<string>?>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtToken { RawToken = "test.token.sig~" });
    }
}
