// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
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

public class PresentationRequestServiceTests
{
    private readonly Mock<ICredentialStore> _storeMock = new();
    private readonly Mock<ILogger<PresentationRequestService>> _loggerMock = new();
    private readonly Mock<ISdJwtService> _sdJwtMock = new();
    private readonly Mock<IWalletRepository> _walletRepoMock = new();
    private readonly PresentationRequestService _service;

    public PresentationRequestServiceTests()
    {
        // Feature 093 round 2: ISdJwtService and IServiceScopeFactory are now REQUIRED
        // dependencies of PresentationRequestService — the previous nullable fallback
        // silently bypassed crypto verification, which was the bug spec 093 closes.
        // These legacy tests supply default mocks that pass-through every verification
        // so the existing test scenarios (type mismatch, expiry, status, claim-value
        // matching against the server-side row) continue to exercise the same logic.
        var services = new ServiceCollection();
        services.AddScoped(_ => _walletRepoMock.Object);
        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        // Default ISdJwtService mock: every verification succeeds with empty claims
        // and a null Issuer. Returning null Issuer skips the iss-vs-credential.IssuerDid
        // check (verifier only enforces it when the verified token has an iss claim) so
        // legacy tests that exercise type/status/expiry/issuer-list/claim-value branches
        // continue to work without per-test mock plumbing. Tests that need claim content
        // or specific iss values use the SetupVerifierClaims helper below.
        _sdJwtMock
            .Setup(s => s.VerifyPresentationAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtVerificationResult
            {
                IsValid = true,
                Issuer = null,
                Claims = new Dictionary<string, object>()
            });

        // Default IWalletRepository mock: any address resolves to a wallet with a
        // valid base64-encoded public key so the verifier's wallet lookup succeeds.
        _walletRepoMock
            .Setup(r => r.GetByAddressAsync(
                It.IsAny<string>(), false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletEntity
            {
                Address = "default-address",
                EncryptedPrivateKey = "not-used",
                EncryptionKeyId = "dev-key",
                Algorithm = "ED25519",
                Owner = "test-owner",
                Tenant = "test-tenant",
                Name = "Test Wallet",
                PublicKey = Convert.ToBase64String(new byte[32])
            });

        _service = new PresentationRequestService(
            _storeMock.Object,
            _loggerMock.Object,
            _sdJwtMock.Object,
            scopeFactory);
    }

    /// <summary>
    /// Helper for tests that need the verifier mock to surface specific claims.
    /// Resets the default no-claims setup with a per-test claims dict.
    /// </summary>
    private void SetupVerifierClaims(Dictionary<string, object> claims, string issuer = "did:sorcha:w:hse")
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
                Issuer = issuer,
                Claims = claims
            });
    }

    // --- CreateRequestAsync ---

    [Fact]
    public async Task CreateRequestAsync_ValidDto_ReturnsRequestWithNonce()
    {
        var dto = CreateTestDto();
        var request = await _service.CreateRequestAsync(dto);

        request.Should().NotBeNull();
        request.Id.Should().NotBeNullOrEmpty();
        request.Nonce.Should().HaveLength(32);
        request.CredentialType.Should().Be("ChemicalHandlingLicense");
        request.Status.Should().Be(PresentationStatus.Pending);
        request.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateRequestAsync_CustomTtl_SetsExpiryCorrectly()
    {
        var dto = CreateTestDto();
        dto = new CreatePresentationRequestDto
        {
            CredentialType = dto.CredentialType,
            CallbackUrl = dto.CallbackUrl,
            TtlSeconds = 60
        };

        var request = await _service.CreateRequestAsync(dto);

        request.ExpiresAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddSeconds(60), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateRequestAsync_EachRequestHasUniqueNonce()
    {
        var dto = CreateTestDto();
        var r1 = await _service.CreateRequestAsync(dto);
        var r2 = await _service.CreateRequestAsync(dto);

        r1.Nonce.Should().NotBe(r2.Nonce);
        r1.Id.Should().NotBe(r2.Id);
    }

    [Fact]
    public async Task CreateRequestAsync_WithAcceptedIssuers_PreservesIssuers()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            AcceptedIssuers = ["did:sorcha:w:issuer-1", "did:sorcha:w:issuer-2"]
        };

        var request = await _service.CreateRequestAsync(dto);

        request.AcceptedIssuers.Should().HaveCount(2);
        request.AcceptedIssuers.Should().Contain("did:sorcha:w:issuer-1");
    }

    [Fact]
    public async Task CreateRequestAsync_WithTargetWalletAddress_PreservesAddress()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            TargetWalletAddress = "wallet-target-1"
        };

        var request = await _service.CreateRequestAsync(dto);

        request.TargetWalletAddress.Should().Be("wallet-target-1");
    }

    [Fact]
    public async Task CreateRequestAsync_DefaultTtl_ExpiresIn300Seconds()
    {
        var dto = CreateTestDto();
        var before = DateTimeOffset.UtcNow.AddSeconds(300);

        var request = await _service.CreateRequestAsync(dto);

        request.ExpiresAt.Should().BeCloseTo(before, TimeSpan.FromSeconds(5));
    }

    // --- GetRequestAsync ---

    [Fact]
    public async Task GetRequestAsync_ExistingRequest_ReturnsRequest()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());
        var fetched = await _service.GetRequestAsync(request.Id);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(request.Id);
    }

    [Fact]
    public async Task GetRequestAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetRequestAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRequestAsync_ExpiredRequest_TransitionsToExpired()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "Test",
            CallbackUrl = "https://example.com/callback",
            TtlSeconds = -1 // Already expired
        };

        var request = await _service.CreateRequestAsync(dto);
        var fetched = await _service.GetRequestAsync(request.Id);

        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be(PresentationStatus.Expired);
    }

    [Fact]
    public async Task GetRequestAsync_VerifiedRequest_DoesNotTransitionToExpired()
    {
        // Create a request, verify it, then check that get doesn't overwrite status
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse"));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _service.SubmitPresentationAsync(request.Id, "cred-1", [], "vp-token");

        // Even though time may pass, verified status should not transition
        var fetched = await _service.GetRequestAsync(request.Id);
        fetched!.Status.Should().Be(PresentationStatus.Verified);
    }

    // --- FindMatchingCredentialsAsync ---

    [Fact]
    public async Task FindMatchingCredentialsAsync_MatchingCredentials_ReturnsInfo()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.MatchAsync("wallet-1", "ChemicalHandlingLicense", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse")
            ]);

        var matches = await _service.FindMatchingCredentialsAsync(request, "wallet-1");

        matches.Should().HaveCount(1);
        matches[0].CredentialId.Should().Be("cred-1");
        matches[0].Type.Should().Be("ChemicalHandlingLicense");
    }

    [Fact]
    public async Task FindMatchingCredentialsAsync_WithRequiredClaims_IncludesRequestedClaims()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            RequiredClaims = [new ClaimConstraint { ClaimName = "class" }]
        };
        var request = await _service.CreateRequestAsync(dto);

        // DisclosableClaims is now derived from RawToken via SdJwtClaimProjection
        // (Task 2), not from ClaimsJson.Keys — so the fixture needs a real SD-JWT
        // with "class" and "permitNumber" as selectively disclosable, not the
        // dummy CreateTestCredential default which projects to Empty.
        var rawToken = BuildSdJwtWithDisclosableClaims(("class", "CategoryB"), ("permitNumber", "HSE-001"));

        _storeMock
            .Setup(s => s.MatchAsync("wallet-1", "ChemicalHandlingLicense", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse",
                    """{"class":"CategoryB","permitNumber":"HSE-001"}""", rawToken)
            ]);

        var matches = await _service.FindMatchingCredentialsAsync(request, "wallet-1");

        matches.Should().HaveCount(1);
        matches[0].RequestedClaims.Should().Contain("class");
        matches[0].DisclosableClaims.Should().Contain("class");
        matches[0].DisclosableClaims.Should().Contain("permitNumber");
    }

    [Fact]
    public async Task FindMatchingCredentialsAsync_StaleFlattenedStoredClaimsJson_DoesNotFalselyMatchLeakedTopLevelClaim()
    {
        // Guards the same class of defect as the n1 "_sd digest array" bug, one
        // step further down the pipeline: a credential ingested BEFORE the nested
        // SD-JWT decoder fix has a stored ClaimsJson where "town" (which really
        // lives nested inside "address") leaked out as a flat TOP-LEVEL key. A
        // verifier requesting "town" by name must NOT get a false-positive match
        // just because the stale stored blob happens to carry that key — the raw
        // token (correctly nested) is the source of truth for what the schema
        // actually looks like.
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            RequiredClaims = [new ClaimConstraint { ClaimName = "town" }]
        };
        var request = await _service.CreateRequestAsync(dto);

        var rawToken = BuildNestedAddressSdJwt();
        var staleFlattenedClaimsJson =
            """{"email":"stuart@stuartfraser.net","town":"Edinburgh","line1":"6/2 Warrender Park Terrace","address":{"_sd":["stale-digest"]}}""";

        _storeMock
            .Setup(s => s.MatchAsync("wallet-1", "ChemicalHandlingLicense", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse",
                    staleFlattenedClaimsJson, rawToken)
            ]);

        var matches = await _service.FindMatchingCredentialsAsync(request, "wallet-1");

        matches.Should().HaveCount(1);
        // The real (raw-token-derived) schema has no top-level "town" — it's nested
        // inside "address" — so the request for "town" must NOT be reported matched.
        matches[0].RequestedClaims.Should().NotContain("town");
    }

    [Fact]
    public async Task FindMatchingCredentialsAsync_NoMatchingCredentials_ReturnsEmptyList()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.MatchAsync("wallet-1", "ChemicalHandlingLicense", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var matches = await _service.FindMatchingCredentialsAsync(request, "wallet-1");

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task FindMatchingCredentialsAsync_MultipleCredentials_ReturnsAll()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.MatchAsync("wallet-1", "ChemicalHandlingLicense", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse"),
                CreateTestCredential("cred-2", "ChemicalHandlingLicense", "did:sorcha:w:epa")
            ]);

        var matches = await _service.FindMatchingCredentialsAsync(request, "wallet-1");

        matches.Should().HaveCount(2);
        matches[0].CredentialId.Should().Be("cred-1");
        matches[1].CredentialId.Should().Be("cred-2");
    }

    [Fact]
    public async Task FindMatchingCredentialsAsync_CredentialWithInvalidClaimsJson_SkipsCredential()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        var badCred = CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse");
        badCred.ClaimsJson = "not-valid-json{{{";

        _storeMock
            .Setup(s => s.MatchAsync("wallet-1", "ChemicalHandlingLicense", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([badCred]);

        var matches = await _service.FindMatchingCredentialsAsync(request, "wallet-1");

        matches.Should().BeEmpty();
    }

    // --- SubmitPresentationAsync ---

    [Fact]
    public async Task SubmitPresentationAsync_ValidPresentation_VerifiedStatus()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse"));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", ["class"], "vp-token");

        result.Status.Should().Be(PresentationStatus.Verified);
        result.VpToken.Should().Be("vp-token");
        result.VerificationResult.Should().NotBeNullOrEmpty();

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitPresentationAsync_TypeMismatch_DeniedStatus()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "DifferentType", "did:sorcha:w:hse"));

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeFalse();
        verification.Errors.Should().Contain(e => e.FailureReason == "TypeMismatch");
    }

    [Fact]
    public async Task SubmitPresentationAsync_RevokedCredential_DeniedStatus()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());
        var cred = CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse");
        cred.Status = CredentialStatus.Revoked;

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cred);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "Revoked");
    }

    [Fact]
    public async Task SubmitPresentationAsync_ExpiredCredential_DeniedStatus()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());
        var cred = CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse");
        cred.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cred);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "Expired");
    }

    [Fact]
    public async Task SubmitPresentationAsync_UntrustedIssuer_DeniedStatus()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            AcceptedIssuers = ["did:sorcha:w:trusted-issuer"]
        };
        var request = await _service.CreateRequestAsync(dto);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:untrusted"));

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "UntrustedIssuer");
    }

    [Fact]
    public async Task SubmitPresentationAsync_CredentialNotFound_DeniedStatus()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialEntity?)null);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "missing", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);
    }

    [Fact]
    public async Task SubmitPresentationAsync_NonexistentRequest_ThrowsKeyNotFoundException()
    {
        var act = () => _service.SubmitPresentationAsync(
            "missing", "cred-1", [], "vp-token");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SubmitPresentationAsync_ExpiredRequest_ThrowsInvalidOperation()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "Test",
            CallbackUrl = "https://example.com/callback",
            TtlSeconds = -1
        };
        var request = await _service.CreateRequestAsync(dto);

        var act = () => _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task SubmitPresentationAsync_RecordsPresentationUsage()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse"));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _service.SubmitPresentationAsync(request.Id, "cred-1", [], "vp-token");

        _storeMock.Verify(
            s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitPresentationAsync_RequiredClaimMissing_DeniedWithMissingClaimError()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            RequiredClaims = [new ClaimConstraint { ClaimName = "nonExistentClaim" }]
        };
        var request = await _service.CreateRequestAsync(dto);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse",
                """{"class":"CategoryB"}"""));

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "MissingClaim");
    }

    [Fact]
    public async Task SubmitPresentationAsync_ClaimValueMismatch_DeniedWithMismatchError()
    {
        // Round 2: claim values come from the verified token. The mock surfaces
        // the same value the credential row would have produced (CategoryB),
        // which mismatches the requested CategoryA.
        SetupVerifierClaims(new Dictionary<string, object>
        {
            ["class"] = "CategoryB"
        });

        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            RequiredClaims = [new ClaimConstraint { ClaimName = "class", ExpectedValue = "CategoryA" }]
        };
        var request = await _service.CreateRequestAsync(dto);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse",
                """{"class":"CategoryB"}"""));

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.Errors.Should().Contain(e => e.FailureReason == "ClaimValueMismatch");
    }

    [Fact]
    public async Task SubmitPresentationAsync_ValidWithVerifiedClaims_IncludesClaimsInResult()
    {
        // Round 2 fix: claim values now come from the verified token, not the
        // server-side credential row. The verifier mock must surface the same
        // claims the credential row would have produced under pre-fix behaviour.
        SetupVerifierClaims(new Dictionary<string, object>
        {
            ["class"] = "CategoryB",
            ["permitNumber"] = "HSE-001"
        });

        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse",
                """{"class":"CategoryB","permitNumber":"HSE-001"}"""));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", ["class"], "vp-token");

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeTrue();
        verification.VerifiedClaims.Should().NotBeNull();
        verification.VerifiedClaims.Should().ContainKey("class");
        verification.VerifiedClaims.Should().ContainKey("permitNumber");
    }

    [Fact]
    public async Task SubmitPresentationAsync_MultipleErrors_AllErrorsReported()
    {
        // Credential has wrong type AND is expired — both errors should appear
        var request = await _service.CreateRequestAsync(CreateTestDto());
        var cred = CreateTestCredential("cred-1", "WrongType", "did:sorcha:w:hse");
        cred.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cred);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Denied);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeFalse();
        verification.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
        verification.Errors.Should().Contain(e => e.FailureReason == "TypeMismatch");
        verification.Errors.Should().Contain(e => e.FailureReason == "Expired");
    }

    [Fact]
    public async Task SubmitPresentationAsync_TrustedIssuer_Verified()
    {
        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            AcceptedIssuers = ["did:sorcha:w:trusted-issuer"]
        };
        var request = await _service.CreateRequestAsync(dto);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:trusted-issuer"));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        result.Status.Should().Be(PresentationStatus.Verified);
    }

    [Fact]
    public async Task SubmitPresentationAsync_RequiredClaimWithMatchingValue_Verified()
    {
        // Round 2: claim values come from the verified token. The mock surfaces
        // the same value the test asserts against.
        SetupVerifierClaims(new Dictionary<string, object>
        {
            ["class"] = "CategoryB"
        });

        var dto = new CreatePresentationRequestDto
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://example.com/callback",
            RequiredClaims = [new ClaimConstraint { ClaimName = "class", ExpectedValue = "CategoryB" }]
        };
        var request = await _service.CreateRequestAsync(dto);

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse",
                """{"class":"CategoryB"}"""));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.SubmitPresentationAsync(
            request.Id, "cred-1", ["class"], "vp-token");

        result.Status.Should().Be(PresentationStatus.Verified);

        var verification = JsonSerializer.Deserialize<VerificationResult>(result.VerificationResult!);
        verification!.IsValid.Should().BeTrue();
        verification.VerifiedClaims.Should().ContainKey("class");
    }

    // --- DenyRequestAsync ---

    [Fact]
    public async Task DenyRequestAsync_PendingRequest_SetsToDenied()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());
        var result = await _service.DenyRequestAsync(request.Id);

        result.Should().NotBeNull();
        result!.Status.Should().Be(PresentationStatus.Denied);
    }

    [Fact]
    public async Task DenyRequestAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.DenyRequestAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DenyRequestAsync_AlreadyVerifiedRequest_DoesNotChangeToDenied()
    {
        // Verify a request first, then try to deny it
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse"));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _service.SubmitPresentationAsync(request.Id, "cred-1", [], "vp-token");

        // Try to deny after verification — should not change status
        var result = await _service.DenyRequestAsync(request.Id);

        result.Should().NotBeNull();
        result!.Status.Should().Be(PresentationStatus.Verified);
    }

    [Fact]
    public async Task DenyRequestAsync_AlreadyDeniedRequest_StaysDenied()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        // Deny once
        await _service.DenyRequestAsync(request.Id);

        // Deny again — should still be denied, no error
        var result = await _service.DenyRequestAsync(request.Id);

        result.Should().NotBeNull();
        result!.Status.Should().Be(PresentationStatus.Denied);
    }

    // --- Replay prevention ---

    [Fact]
    public async Task SubmitPresentationAsync_DoubleSubmit_ThrowsInvalidOperation()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        _storeMock
            .Setup(s => s.GetByIdAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCredential("cred-1", "ChemicalHandlingLicense", "did:sorcha:w:hse"));

        _storeMock
            .Setup(s => s.RecordPresentationAsync("cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // First submit succeeds
        await _service.SubmitPresentationAsync(request.Id, "cred-1", [], "vp-token");

        // Second submit should fail (request is no longer Pending)
        var act = () => _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token-2");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Verified*expected Pending*");
    }

    [Fact]
    public async Task SubmitPresentationAsync_AfterDeny_ThrowsInvalidOperation()
    {
        var request = await _service.CreateRequestAsync(CreateTestDto());

        // Deny the request first
        await _service.DenyRequestAsync(request.Id);

        // Then try to submit — should fail because status is Denied, not Pending
        var act = () => _service.SubmitPresentationAsync(
            request.Id, "cred-1", [], "vp-token");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Denied*expected Pending*");
    }

    // --- Helpers ---

    private static CreatePresentationRequestDto CreateTestDto() => new()
    {
        CredentialType = "ChemicalHandlingLicense",
        CallbackUrl = "https://verifier.example/callback",
        VerifierIdentity = "Test Verifier"
    };

    private static CredentialEntity CreateTestCredential(
        string id, string type, string issuerDid, string? claimsJson = null, string? rawToken = null) => new()
    {
        Id = id,
        Type = type,
        IssuerDid = issuerDid,
        SubjectDid = "did:sorcha:w:holder-1",
        WalletAddress = "wallet-1",
        ClaimsJson = claimsJson ?? """{"class":"CategoryB","permitNumber":"HSE-2026-001"}""",
        // Not a real SD-JWT — fine for every test except DisclosableClaims
        // assertions, which need SdJwtClaimProjection.Project to succeed and
        // therefore must pass an explicit rawToken (see
        // BuildSdJwtWithDisclosableClaims below).
        RawToken = rawToken ?? "eyJhbGciOiJFZERTQSJ9.test",
        Status = CredentialStatus.Active,
        IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
    };

    // --- Minimal SD-JWT construction (RFC 9901 §4.2.1) for DisclosableClaims tests ---

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Disclosure(string salt, string name, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, name, value });
        return B64Url(Encoding.UTF8.GetBytes(json));
    }

    private static string Digest(string disclosure) =>
        B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(disclosure)));

    /// <summary>
    /// Builds a compact SD-JWT whose body has an <c>_sd</c> digest for each
    /// (name, value) pair — i.e. every named claim is selectively disclosable —
    /// so <see cref="Sorcha.Wallet.Service.Services.Implementation.SdJwtClaimProjection"/>
    /// reports it in <c>DisclosableClaims</c>.
    /// </summary>
    private static string BuildSdJwtWithDisclosableClaims(params (string Name, object Value)[] claims)
    {
        var disclosures = claims
            .Select((c, i) => Disclosure($"salt{i}", c.Name, c.Value))
            .ToArray();

        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"ES256","typ":"dc+sd-jwt"}"""));
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/test/v1",
            ["iss"] = "did:sorcha:w:hse",
            ["_sd"] = disclosures.Select(Digest).ToArray()
        };
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(body));
        var jwt = $"{header}.{payload}.c2ln"; // signature is never verified on this path

        return jwt + "~" + string.Join("~", disclosures);
    }

    /// <summary>
    /// Builds a compact SD-JWT with a NESTED disclosure for "address" (town +
    /// line1 individually disclosable children) — the shape that a pre-fix
    /// decoder (top-level-only _sd resolution) mangled, leaving "address" as a
    /// raw digest array while its children leaked out as flat top-level claims.
    /// </summary>
    private static string BuildNestedAddressSdJwt()
    {
        var town = Disclosure("s1", "town", "Edinburgh");
        var line1 = Disclosure("s2", "line1", "6/2 Warrender Park Terrace");
        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"ES256","typ":"dc+sd-jwt"}"""));
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/test/v1",
            ["iss"] = "did:sorcha:w:hse",
            ["email"] = "stuart@stuartfraser.net",
            ["address"] = new Dictionary<string, object>
            {
                ["_sd"] = new[] { Digest(town), Digest(line1) }
            }
        };
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(body));
        var jwt = $"{header}.{payload}.c2ln";

        return jwt + "~" + town + "~" + line1;
    }
}
