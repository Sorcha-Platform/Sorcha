// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;

using Factory = Sorcha.Blueprint.Engine.Tests.Credentials.EngineSdJwtTestFactory;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 — revocation now flows through the unified <see cref="ITrustEvaluator"/> +
/// <see cref="IStatusListChecker"/> rather than the engine verifier's old <c>IRevocationChecker</c>
/// branch. The credential carries a real IETF status reference; the fail-closed / fail-open policy
/// comes from the requirement. Tests use real signed SD-JWT VCs and a fake status checker.
/// </summary>
public class CredentialVerifierRevocationTests
{
    private static EngineSdJwtTestFactory.MintedSdJwt MintWithStatus(string issuer = "did:sorcha:issuer:gov") =>
        Factory.MintEs256("LicenseCredential", issuer,
            statusClaim: Factory.IetfStatusClaim("https://issuer/status/1", 5));

    private static CredentialPresentation Present(string credentialId, string raw) =>
        new() { CredentialId = credentialId, RawPresentation = raw };

    [Fact]
    public async Task VerifyAsync_ActiveCredential_Accepted()
    {
        var minted = MintWithStatus();
        var verifier = Factory.BuildVerifier(directory: null, statusChecker: new Factory.FakeStatusChecker(CredentialStatusValue.Valid), minted);
        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].RevocationStatus.Should().Be("Active");
    }

    [Fact]
    public async Task VerifyAsync_RevokedCredential_Rejected()
    {
        var minted = MintWithStatus();
        var verifier = Factory.BuildVerifier(directory: null, statusChecker: new Factory.FakeStatusChecker(CredentialStatusValue.Invalid), minted);
        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.FailureReason == CredentialFailureReason.Revoked);
    }

    [Fact]
    public async Task VerifyAsync_UnavailableStatus_FailClosed_Blocked()
    {
        var minted = MintWithStatus();
        var verifier = Factory.BuildVerifier(directory: null, statusChecker: new Factory.FakeStatusChecker(CredentialStatusValue.Unresolved), minted);
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential", RevocationCheckPolicy = RevocationCheckPolicy.FailClosed }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.FailureReason == CredentialFailureReason.RevocationCheckUnavailable);
    }

    [Fact]
    public async Task VerifyAsync_UnavailableStatus_FailOpen_Accepted()
    {
        var minted = MintWithStatus();
        var verifier = Factory.BuildVerifier(directory: null, statusChecker: new Factory.FakeStatusChecker(CredentialStatusValue.Unresolved), minted);
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential", RevocationCheckPolicy = RevocationCheckPolicy.FailOpen }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
    }

    [Fact]
    public async Task VerifyAsync_StatusClaimButNoChecker_FailClosed_Blocked()
    {
        var minted = MintWithStatus();
        // No status checker wired — a credential that carries a status reference cannot be cleared.
        var verifier = Factory.BuildVerifier(minted);
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential", RevocationCheckPolicy = RevocationCheckPolicy.FailClosed }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.FailureReason == CredentialFailureReason.RevocationCheckUnavailable);
    }

    [Fact]
    public async Task VerifyAsync_NoStatusClaim_Accepted()
    {
        // No status reference on the credential → no revocation check → accepted.
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov");
        var verifier = Factory.BuildVerifier(minted);
        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials[0].RevocationStatus.Should().Be("Active");
    }
}
