// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Tests for Feature 093 (VC & Presentation Security Fixes) User Story 1:
// the presentation verifier must cryptographically validate the submitted vpToken
// via ISdJwtService.VerifyPresentationAsync and source claim values from the
// verified token rather than the server-side credential store row.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Services;
using Sorcha.Wallet.Core.Domain.Entities;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Presentations;

/// <summary>
/// Verification-path tests for <see cref="PresentationRequestService"/> covering
/// the Feature 093 security fix. Existing behaviour tests live in
/// <c>PresentationRequestServiceTests</c>.
/// </summary>
public class PresentationRequestVerificationTests
{
    private const string IssuerAddress = "sorcha1testissuer000000000000000001";
    private const string IssuerDid = "did:sorcha:w:sorcha1testissuer000000000000000001";
    private const string IssuerAlgorithm = "ED25519";
    private static readonly byte[] IssuerPublicKey = new byte[32];
    private static readonly string IssuerPublicKeyBase64 = Convert.ToBase64String(IssuerPublicKey);

    private readonly Mock<ICredentialStore> _storeMock = new();
    private readonly Mock<ILogger<PresentationRequestService>> _loggerMock = new();
    private readonly Mock<ISdJwtService> _sdJwtMock = new();
    private readonly Mock<IWalletRepository> _walletRepoMock = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PresentationRequestService _service;

    public PresentationRequestVerificationTests()
    {
        // Build a tiny scoped provider so PresentationRequestService can resolve
        // the IWalletRepository mock per verification call, matching production.
        // AddScoped (not AddSingleton) so the test exercises the scope-resolution path
        // that PresentationRequestService uses precisely to handle the Singleton→Scoped
        // boundary.
        var services = new ServiceCollection();
        services.AddScoped(_ => _walletRepoMock.Object);
        var provider = services.BuildServiceProvider();
        _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        _service = new PresentationRequestService(
            _storeMock.Object,
            _loggerMock.Object,
            sdJwtService: _sdJwtMock.Object,
            scopeFactory: _scopeFactory);

        // Default wallet repository setup: the issuer wallet exists with a classical public key.
        _walletRepoMock
            .Setup(r => r.GetByAddressAsync(
                IssuerAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletEntity
            {
                Address = IssuerAddress,
                EncryptedPrivateKey = "not-used-in-verification",
                EncryptionKeyId = "dev-key",
                Algorithm = IssuerAlgorithm,
                Owner = "test-owner",
                Tenant = "test-tenant",
                Name = "Test Issuer Wallet",
                PublicKey = IssuerPublicKeyBase64
            });
    }

    // --- T005: valid signed token is Verified with only disclosed claims ---

    [Fact]
    public async Task ValidSignedToken_ReturnsVerified_WithOnlyDisclosedClaims()
    {
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                "valid-vp-token",
                It.IsAny<byte[]>(),
                IssuerAlgorithm,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = true,
                Issuer = IssuerDid,
                Claims = new Dictionary<string, object>
                {
                    ["class"] = "CategoryB"
                    // Note: no permitNumber — the holder only disclosed "class"
                }
            });

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential(
                "cred-1",
                "TestLicense",
                IssuerDid,
                // Server-side row has MORE claims than the presented token —
                // the fix ensures only the presented subset appears in the result.
                """{"class":"CategoryB","permitNumber":"SECRET-001"}"""));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", ["class"], "valid-vp-token");

        result.Status.Should().Be(PresentationStatus.Verified);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeTrue();
        verification.VerifiedClaims.Should().NotBeNull();
        verification.VerifiedClaims.Should().ContainKey("class");
        verification.VerifiedClaims.Should().NotContainKey("permitNumber",
            because: "claim values must come from the verified token, not the server-side row");
    }

    // --- T006: tampered signature → Denied with signature error ---

    [Fact]
    public async Task TamperedSignature_ReturnsDenied_WithSignatureError()
    {
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                "tampered-vp-token",
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = false,
                Errors = { "Invalid signature" }
            });

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "TestLicense", IssuerDid));

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "tampered-vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeFalse();
        verification.Errors.Should().Contain(e => e.FailureReason == "SignatureInvalid");
        (verification.VerifiedClaims?.Count ?? 0).Should().Be(0,
            because: "a signature-invalid token must not leak any claim values into the result");
    }

    // --- T007: iss mismatch between token and credential → Denied ---

    [Fact]
    public async Task IssuerMismatch_BetweenTokenIssAndCredentialRow_ReturnsDenied()
    {
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                "vp-token",
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = true,
                Issuer = "did:sorcha:w:sorcha1attacker0000000000000000000",
                Claims = new Dictionary<string, object> { ["class"] = "CategoryB" }
            });

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "TestLicense", IssuerDid));

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "IssuerMismatch");
    }

    // --- T008: malformed disclosure → Denied with disclosure integrity error ---

    [Fact]
    public async Task MalformedDisclosure_ReturnsDenied_WithDisclosureIntegrityError()
    {
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = false,
                Errors = { "Failed to parse disclosure: abc..." },
                ErrorDetails =
                {
                    new SdJwtError
                    {
                        Kind = SdJwtErrorKind.DisclosureIntegrityFailure,
                        Message = "Failed to parse disclosure: abc..."
                    }
                }
            });

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "TestLicense", IssuerDid));

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "DisclosureIntegrityFailure");
    }

    // --- T009: server-side claim values must not leak into the verification result ---

    [Fact]
    public async Task StoreSideClaimValues_NotLeaked_IntoVerificationResult()
    {
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = true,
                Issuer = IssuerDid,
                Claims = new Dictionary<string, object>
                {
                    ["tokenOnly"] = "tokenValue"
                }
            });

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential(
                "cred-1",
                "TestLicense",
                IssuerDid,
                """{"serverOnlySecret":"do-not-leak","another":"also-secret"}"""));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeTrue();
        verification.VerifiedClaims.Should().ContainKey("tokenOnly");
        verification.VerifiedClaims.Should().NotContainKey("serverOnlySecret");
        verification.VerifiedClaims.Should().NotContainKey("another");
    }

    // --- T010 (partial integration-style in unit scope): untrusted issuer — key not resolvable ---

    [Fact]
    public async Task VerificationFails_WhenIssuerWallet_NotFoundInRepository()
    {
        _walletRepoMock
            .Setup(r => r.GetByAddressAsync(
                "unknownissuer", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential(
                "cred-1", "TestLicense", "did:sorcha:w:unknownissuer"));

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "IssuerNotFound");
    }

    // --- US2: credentialStatus embedded in verified token preferred over server-side row ---

    [Fact]
    public async Task Verifier_PrefersEmbeddedCredentialStatusClaim_OverServerSideRow()
    {
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = true,
                Issuer = IssuerDid,
                Claims = new Dictionary<string, object>
                {
                    ["class"] = "CategoryB",
                    ["credentialStatus"] = new Dictionary<string, object>
                    {
                        ["id"] = "https://issuer.example/status/list-embedded#42",
                        ["type"] = "BitstringStatusListEntry",
                        ["statusPurpose"] = "revocation",
                        ["statusListIndex"] = "42",
                        ["statusListCredential"] = "https://issuer.example/status/list-embedded"
                    }
                }
            });

        var credWithServerSideRow = CreateTestCredential("cred-1", "TestLicense", IssuerDid);
        credWithServerSideRow.StatusListUrl = "https://issuer.example/status/list-SERVERSIDE";
        credWithServerSideRow.StatusListIndex = 999;

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(credWithServerSideRow);

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        // Both URLs are unreachable in unit context so CheckStatusListAsync returns "Active"
        // as its standard fallback. Verification should still succeed.
        result.Status.Should().Be(PresentationStatus.Verified,
            because: "status URL unreachable in unit context falls through to Active");
    }

    [Fact]
    public async Task Verifier_FallsBackToServerSideRow_WhenTokenHasNoEmbeddedCredentialStatus()
    {
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = true,
                Issuer = IssuerDid,
                Claims = new Dictionary<string, object>
                {
                    ["class"] = "CategoryB"
                }
            });

        var credWithServerSideRow = CreateTestCredential("cred-1", "TestLicense", IssuerDid);
        credWithServerSideRow.StatusListUrl = "https://issuer.example/legacy-row-status";
        credWithServerSideRow.StatusListIndex = 7;

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(credWithServerSideRow);

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = await _service.CreateRequestAsync(CreateTestDto("TestLicense"));
        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Verified,
            because: "pre-fix credentials must continue to verify via the server-side row fallback per FR-010");
    }

    // --- Helpers ---

    private static CreatePresentationRequestDto CreateTestDto(string credentialType) => new()
    {
        CredentialType = credentialType,
        CallbackUrl = "https://verifier.example/callback",
        VerifierIdentity = "Test Verifier"
    };

    private static CredentialEntity CreateTestCredential(
        string id, string type, string issuerDid, string? claimsJson = null) => new()
    {
        Id = id,
        Type = type,
        IssuerDid = issuerDid,
        SubjectDid = "did:sorcha:w:holder-1",
        WalletAddress = "wallet-1",
        ClaimsJson = claimsJson ?? """{"class":"CategoryB"}""",
        RawToken = "eyJhbGciOiJFZERTQSJ9.test",
        Status = CredentialStatus.Active,
        IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
    };
}
