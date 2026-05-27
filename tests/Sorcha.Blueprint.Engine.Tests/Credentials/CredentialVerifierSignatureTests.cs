// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Models.Credentials;

using Factory = Sorcha.Blueprint.Engine.Tests.Credentials.EngineSdJwtTestFactory;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 / T020 (SC-002) — proves the engine verifier reports signature validity truthfully.
/// The retired behaviour hard-coded <c>SignatureValid=false</c> and never actually verified; here a
/// genuinely-signed credential is accepted with <c>SignatureValid=true</c>, and a credential whose
/// signature is tampered (but whose issuer would otherwise be trusted) is rejected
/// <see cref="CredentialFailureReason.InvalidSignature"/>.
/// </summary>
public class CredentialVerifierSignatureTests
{
    private static CredentialPresentation Present(string credentialId, string raw) =>
        new() { CredentialId = credentialId, RawPresentation = raw };

    [Fact]
    public async Task VerifyAsync_GenuinelySigned_Accepted_SignatureValidTrue()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var verifier = Factory.BuildVerifier(minted);

        var result = await verifier.VerifyAsync(
            [new CredentialRequirement { Type = "LicenseCredential" }],
            [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().ContainSingle();
        result.VerifiedCredentials[0].SignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_TamperedSignature_Rejected_InvalidSignature()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        // The key resolver still resolves the issuer key for the tampered token (keyed on
        // header.payload), so it is the signature check itself — not a missing key — that rejects it.
        var verifier = Factory.BuildVerifier(minted);
        var tampered = Factory.TamperSignature(minted.Raw);

        var result = await verifier.VerifyAsync(
            [new CredentialRequirement { Type = "LicenseCredential" }],
            [Present("cred-1", tampered)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.FailureReason == CredentialFailureReason.InvalidSignature);
    }

    [Fact]
    public async Task VerifyAsync_TamperedSignature_NotAcceptedEvenUnderPermissivePolicy()
    {
        // Default (null) policy → register@Low, which would trust this issuer if the signature held.
        // The signature precondition fails closed regardless: trust is never granted on bad crypto.
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov");
        var verifier = Factory.BuildVerifier(minted);
        var tampered = Factory.TamperSignature(minted.Raw);

        var result = await verifier.VerifyAsync(
            [new CredentialRequirement { Type = "LicenseCredential", TrustPolicy = null }],
            [Present("cred-1", tampered)]);

        result.IsValid.Should().BeFalse();
        result.VerifiedCredentials.Should().BeEmpty();
    }
}
