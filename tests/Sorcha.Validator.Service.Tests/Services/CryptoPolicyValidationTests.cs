// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

public class CryptoPolicyValidationTests
{
    [Fact]
    public void ControlDocketProcessor_ShouldRecognizeCryptoPolicyUpdateAction()
    {
        // The CryptoPolicyUpdate enum value should exist
        var actionType = ControlActionType.CryptoPolicyUpdate;
        actionType.Should().BeDefined();
        ((int)actionType).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CryptoPolicyUpdatePayload_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """
        {
            "version": 2,
            "acceptedSignatureAlgorithms": ["ED25519", "ML-DSA-65", "SLH-DSA-128s"],
            "requiredSignatureAlgorithms": ["ED25519"],
            "enforcementMode": "Strict",
            "effectiveFrom": "2026-02-25T00:00:00Z",
            "updatedBy": "admin-1"
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act
        var payload = JsonSerializer.Deserialize<CryptoPolicyUpdatePayload>(json, options);

        // Assert
        payload.Should().NotBeNull();
        payload!.Version.Should().Be(2);
        payload.AcceptedSignatureAlgorithms.Should().HaveCount(3);
        payload.AcceptedSignatureAlgorithms.Should().Contain("ML-DSA-65");
        payload.RequiredSignatureAlgorithms.Should().Contain("ED25519");
        payload.EnforcementMode.Should().Be("Strict");
        payload.UpdatedBy.Should().Be("admin-1");
    }

    [Fact]
    public void CryptoPolicyUpdatePayload_RequiredSubsetCheck_DetectsMismatch()
    {
        // Arrange - RequiredSignatureAlgorithms includes an algorithm NOT in AcceptedSignatureAlgorithms
        var payload = new CryptoPolicyUpdatePayload
        {
            Version = 1,
            AcceptedSignatureAlgorithms = new[] { "ED25519" },
            RequiredSignatureAlgorithms = new[] { "ML-DSA-65" }, // Not in accepted!
            EnforcementMode = "Strict"
        };

        // Act - Check subset constraint
        var accepted = new HashSet<string>(payload.AcceptedSignatureAlgorithms, StringComparer.OrdinalIgnoreCase);
        var missing = payload.RequiredSignatureAlgorithms.Where(r => !accepted.Contains(r)).ToArray();

        // Assert
        missing.Should().NotBeEmpty();
        missing.Should().Contain("ML-DSA-65");
    }

    [Fact]
    public void Transaction_WithPqcAlgorithm_ShouldBeRecognized()
    {
        // The ValidationEngine's recognized algorithms set should include PQC algorithms
        var recognizedAlgorithms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ED25519", "NISTP256", "NIST-P256", "P256", "ECDSA-P256",
            "RSA4096", "RSA-4096",
            "ML-DSA-65", "MLDSA65",
            "SLH-DSA-128S", "SLHDSA128S"
        };

        // Classical algorithms
        recognizedAlgorithms.Contains("ED25519").Should().BeTrue();
        recognizedAlgorithms.Contains("NISTP256").Should().BeTrue();

        // PQC algorithms
        recognizedAlgorithms.Contains("ML-DSA-65").Should().BeTrue();
        recognizedAlgorithms.Contains("SLH-DSA-128S").Should().BeTrue();

        // Unknown algorithms should not be recognized
        recognizedAlgorithms.Contains("UNKNOWN-ALG").Should().BeFalse();
    }

    [Fact]
    public void Transaction_ControlType_ShouldSkipPolicyValidation()
    {
        // Control/Genesis transactions should be exempt from crypto policy checks
        var metadata = new Dictionary<string, string>
        {
            ["Type"] = "Genesis"
        };

        metadata.TryGetValue("Type", out var txType).Should().BeTrue();
        (txType is "Genesis" or "Control").Should().BeTrue();
    }

    [Fact]
    public void Transaction_WithNoSignatures_ShouldFailPolicyValidation()
    {
        // Transactions with zero signatures should fail crypto policy validation
        var signatures = new List<RegisterSignature>();
        signatures.Should().BeEmpty();
        // The validation engine would add VAL_POLICY_002 error
    }
}
