// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Models.Tests.Credentials;

public class ActionCredentialSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void Serialize_ActionWithCredentialRequirements_IncludesRequirements()
    {
        var action = new Action
        {
            Id = 1,
            Title = "Submit License Application",
            CredentialRequirements =
            [
                new CredentialRequirement
                {
                    Type = "IdentityAttestation",
                    TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov"]),
                    RequiredClaims =
                    [
                        new ClaimConstraint { ClaimName = "nationality" }
                    ],
                    RevocationCheckPolicy = RevocationCheckPolicy.FailClosed
                }
            ]
        };

        var json = JsonSerializer.Serialize(action, Options);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("credentialRequirements", out var reqs).Should().BeTrue();
        reqs.GetArrayLength().Should().Be(1);
        reqs[0].GetProperty("type").GetString().Should().Be("IdentityAttestation");
        reqs[0].GetProperty("trustPolicy").GetProperty("sources")[0].GetProperty("allowedIssuers").GetArrayLength().Should().Be(1);
        reqs[0].GetProperty("requiredClaims")[0].GetProperty("claimName").GetString().Should().Be("nationality");
        reqs[0].GetProperty("revocationCheckPolicy").GetString().Should().Be("FailClosed");
    }

    [Fact]
    public void Serialize_ActionWithCredentialIssuanceConfig_IncludesConfig()
    {
        var action = new Action
        {
            Id = 2,
            Title = "Approve License",
            CredentialIssuanceConfig = new CredentialIssuanceConfig
            {
                CredentialType = "LicenseCredential",
                ClaimMappings =
                [
                    new ClaimMapping { ClaimName = "licenseType", SourceField = "/licenseType" }
                ],
                RecipientParticipantId = "applicant",
                ExpiryDuration = "P365D"
            }
        };

        var json = JsonSerializer.Serialize(action, Options);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("credentialIssuanceConfig", out var config).Should().BeTrue();
        config.GetProperty("credentialType").GetString().Should().Be("LicenseCredential");
        config.GetProperty("claimMappings").GetArrayLength().Should().Be(1);
        config.GetProperty("recipientParticipantId").GetString().Should().Be("applicant");
        config.GetProperty("expiryDuration").GetString().Should().Be("P365D");
    }

    [Fact]
    public void Serialize_ActionWithoutCredentials_OmitsProperties()
    {
        var action = new Action
        {
            Id = 3,
            Title = "Simple Action"
        };

        var json = JsonSerializer.Serialize(action, Options);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("credentialRequirements", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("credentialIssuanceConfig", out _).Should().BeFalse();
    }

    [Fact]
    public void Deserialize_ActionWithCredentialRequirements_RoundTrips()
    {
        var json = """
        {
            "id": 1,
            "title": "Gated Action",
            "credentialRequirements": [
                {
                    "type": "LicenseCredential",
                    "trustPolicy": {
                        "sources": [ { "kind": "did-allowlist", "allowedIssuers": ["did:sorcha:issuer:gov"] } ]
                    },
                    "requiredClaims": [
                        { "claimName": "licenseType", "expectedValue": "A" }
                    ],
                    "revocationCheckPolicy": 0,
                    "description": "Valid license required"
                }
            ]
        }
        """;

        var action = JsonSerializer.Deserialize<Action>(json, Options);

        action.Should().NotBeNull();
        action!.CredentialRequirements.Should().HaveCount(1);
        var req = action.CredentialRequirements!.First();
        req.Type.Should().Be("LicenseCredential");
        req.TrustPolicy.AllowedIssuerDids().Should().ContainSingle().Which.Should().Be("did:sorcha:issuer:gov");
        req.RequiredClaims.Should().HaveCount(1);
        req.RequiredClaims!.First().ClaimName.Should().Be("licenseType");
        req.Description.Should().Be("Valid license required");
    }

    [Fact]
    public void Deserialize_ActionWithIssuanceConfig_RoundTrips()
    {
        var json = """
        {
            "id": 2,
            "title": "Issuing Action",
            "credentialIssuanceConfig": {
                "credentialType": "LicenseCredential",
                "claimMappings": [
                    { "claimName": "licenseType", "sourceField": "/licenseType" }
                ],
                "recipientParticipantId": "applicant",
                "expiryDuration": "P365D",
                "registerId": "license-register"
            }
        }
        """;

        var action = JsonSerializer.Deserialize<Action>(json, Options);

        action.Should().NotBeNull();
        action!.CredentialIssuanceConfig.Should().NotBeNull();
        var config = action.CredentialIssuanceConfig!;
        config.CredentialType.Should().Be("LicenseCredential");
        config.ClaimMappings.Should().HaveCount(1);
        config.RecipientParticipantId.Should().Be("applicant");
        config.ExpiryDuration.Should().Be("P365D");
        config.RegisterId.Should().Be("license-register");
    }

    [Fact]
    public void Serialize_FullRoundTrip_PreservesAllData()
    {
        var original = new Action
        {
            Id = 10,
            Title = "Full Credential Action",
            CredentialRequirements =
            [
                new CredentialRequirement
                {
                    Type = "IdentityAttestation",
                    RequiredClaims = [new ClaimConstraint { ClaimName = "name" }]
                }
            ],
            CredentialIssuanceConfig = new CredentialIssuanceConfig
            {
                CredentialType = "LicenseCredential",
                ClaimMappings =
                [
                    new ClaimMapping { ClaimName = "license", SourceField = "/license" }
                ],
                RecipientParticipantId = "applicant",
                Disclosable = ["license"]
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Action>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(10);
        deserialized.CredentialRequirements.Should().HaveCount(1);
        deserialized.CredentialRequirements!.First().Type.Should().Be("IdentityAttestation");
        deserialized.CredentialIssuanceConfig.Should().NotBeNull();
        deserialized.CredentialIssuanceConfig!.CredentialType.Should().Be("LicenseCredential");
        deserialized.CredentialIssuanceConfig.Disclosable.Should().ContainSingle("license");
    }
}
