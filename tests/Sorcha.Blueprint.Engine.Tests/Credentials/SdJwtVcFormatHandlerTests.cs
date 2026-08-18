// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 / T030 — proves the SD-JWT VC format handler verifies the issuer signature for
/// real and routes the trust decision through <see cref="ITrustEvaluator"/>. No mocked crypto:
/// a tampered token is rejected with <see cref="TrustFailureReason.SignatureInvalid"/>.
/// </summary>
public class SdJwtVcFormatHandlerTests
{
    private static SdJwtVcFormatHandler BuildHandler(
        EngineSdJwtTestFactory.MintedSdJwt minted,
        IStatusListChecker? statusChecker = null,
        IIssuerKeyResolver? keyResolver = null)
    {
        keyResolver ??= new EngineSdJwtTestFactory.FakeIssuerKeyResolver(minted.IssuerPublicKey, minted.Algorithm, minted.Kid);
        var registry = new TrustResolverRegistry(
        [
            new RegisterTrustSourceResolver(new EngineSdJwtTestFactory.AlwaysResolvesDirectory()),
            new DidAllowlistTrustSourceResolver(new EngineSdJwtTestFactory.AlwaysResolvesDirectory())
        ]);
        var evaluator = new TrustEvaluator(registry, statusChecker);
        return new SdJwtVcFormatHandler(new SdJwtService(), keyResolver, evaluator);
    }

    private static PresentedCredential Presented(string raw) =>
        new() { Raw = raw, Format = CredentialFormat.SdJwtVc };

    [Fact]
    public void Format_IsSdJwtVc()
    {
        var minted = EngineSdJwtTestFactory.MintEs256("LicenseCredential", "did:sorcha:org:gov");
        BuildHandler(minted).Format.Should().Be(CredentialFormat.SdJwtVc);
    }

    [Fact]
    public async Task VerifyAsync_ValidSignature_TrustedAndSignatureValid()
    {
        var minted = EngineSdJwtTestFactory.MintEs256(
            "LicenseCredential", "did:sorcha:org:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var handler = BuildHandler(minted);

        var result = await handler.VerifyAsync(
            Presented(minted.Raw),
            new CredentialRequirement { Type = "LicenseCredential" });

        result.IsValid.Should().BeTrue();
        result.Trust.Should().NotBeNull();
        result.Trust!.SignatureValid.Should().BeTrue();
        result.Trust.IsTrusted.Should().BeTrue();
        result.IssuerId.Should().Be("did:sorcha:org:gov");
        result.DisclosedClaims.Should().ContainKey("vct");
        result.DisclosedClaims.Should().ContainKey("license_type");
    }

    [Fact]
    public async Task VerifyAsync_TamperedSignature_RejectedSignatureInvalid()
    {
        var minted = EngineSdJwtTestFactory.MintEs256(
            "LicenseCredential", "did:sorcha:org:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var handler = BuildHandler(minted);
        var tampered = EngineSdJwtTestFactory.TamperSignature(minted.Raw);

        var result = await handler.VerifyAsync(
            Presented(tampered),
            new CredentialRequirement { Type = "LicenseCredential" });

        result.IsValid.Should().BeFalse();
        result.Trust.Should().NotBeNull();
        result.Trust!.SignatureValid.Should().BeFalse();
        result.Trust.FailureReason.Should().Be(TrustFailureReason.SignatureInvalid);
    }

    [Fact]
    public async Task VerifyAsync_KeyUnresolvable_RejectedSignatureInvalid()
    {
        var minted = EngineSdJwtTestFactory.MintEs256("LicenseCredential", "did:sorcha:org:gov");
        var handler = BuildHandler(minted,
            keyResolver: new EngineSdJwtTestFactory.FakeIssuerKeyResolver(minted.IssuerPublicKey, minted.Algorithm) { ReturnNull = true });

        var result = await handler.VerifyAsync(
            Presented(minted.Raw),
            new CredentialRequirement { Type = "LicenseCredential" });

        result.IsValid.Should().BeFalse();
        result.Trust!.SignatureValid.Should().BeFalse();
        result.Trust.FailureReason.Should().Be(TrustFailureReason.SignatureInvalid);
    }

    [Fact]
    public async Task VerifyAsync_RevokedViaStatusList_RejectedRevoked()
    {
        var minted = EngineSdJwtTestFactory.MintEs256(
            "LicenseCredential", "did:sorcha:org:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA" },
            statusClaim: EngineSdJwtTestFactory.IetfStatusClaim("https://issuer/status/1", 7));
        var handler = BuildHandler(minted, statusChecker: new EngineSdJwtTestFactory.FakeStatusChecker(CredentialStatusValue.Invalid));

        var result = await handler.VerifyAsync(
            Presented(minted.Raw),
            new CredentialRequirement { Type = "LicenseCredential" });

        result.IsValid.Should().BeFalse();
        result.Trust!.SignatureValid.Should().BeTrue();
        result.Trust.FailureReason.Should().Be(TrustFailureReason.Revoked);
    }

    [Fact]
    public async Task VerifyAsync_WrongFormatRequested_RejectsFormatUnsupported()
    {
        var minted = EngineSdJwtTestFactory.MintEs256("LicenseCredential", "did:sorcha:org:gov");
        var handler = BuildHandler(minted);

        var result = await handler.VerifyAsync(
            new PresentedCredential { Raw = minted.Raw, Format = CredentialFormat.MsoMdoc },
            new CredentialRequirement { Type = "LicenseCredential", Format = CredentialFormat.MsoMdoc });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }
}
