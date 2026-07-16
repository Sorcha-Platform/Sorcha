// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Wallet.Core.Domain.Enums;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Endpoints;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Pre-signing validation guards on the <c>IssueCredential</c> handler. Spec
/// 093 data-model says <c>statusListCredential</c> MUST be an absolute HTTPS
/// URL — anything embedded in the signed credential is unfixable, so the
/// guard runs before the wallet starts signing. Reflection-based static
/// handler invocation per the established pattern in
/// <see cref="CitizenWalletEnrolEndpointTests"/>.
/// </summary>
public sealed class IssueCredentialStatusListUrlGuardTests
{
    private const string WalletAddress = "ws1qissuer1";

    private readonly Mock<IWalletRepository> _walletRepository = new();

    public IssueCredentialStatusListUrlGuardTests()
    {
        _walletRepository.Setup(r => r.GetByAddressAsync(
                WalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletEntity
            {
                Address = WalletAddress,
                EncryptedPrivateKey = "test-key",
                EncryptionKeyId = "test-kid",
                Algorithm = "ED25519",
                Owner = "test-owner",
                Tenant = "test-tenant",
                Name = "test-wallet",
                Status = WalletStatus.Active
            });
    }

    [Theory]
    [InlineData("http://example.com/status/list1.jwt")]
    [InlineData("ftp://example.com/status/list1.jwt")]
    [InlineData("file:///etc/status.json")]
    public async Task IssueCredential_NonHttpsStatusListUrl_BadRequest(string url)
    {
        var request = new IssueCredentialRequest
        {
            CredentialType = "TestCredential",
            Claims = new Dictionary<string, object> { ["foo"] = "bar" },
            RecipientWallet = "ws1qrecipient1",
            StatusListUrl = url,
            StatusListIndex = 0
        };

        var result = await InvokeAsync(WalletAddress, request);

        result.GetType().Name.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task IssueCredential_HttpsStatusListUrl_PassesGuard()
    {
        // Verifies an HTTPS URL passes the URL guard. With no issuance key wired
        // (IIssuanceKeyService = null), the handler now fails closed with a 409 Problem
        // (Feature 149 — no resolvable VC-issuance key) rather than minting an
        // unverifiable bare-wallet credential. The point of this test is that HTTPS does
        // NOT trip the URL guard's BadRequest; reaching the fail-closed 409 proves it.
        var request = new IssueCredentialRequest
        {
            CredentialType = "TestCredential",
            Claims = new Dictionary<string, object> { ["foo"] = "bar" },
            RecipientWallet = "ws1qrecipient1",
            StatusListUrl = "https://issuer.example.com/status/list1.jwt",
            StatusListIndex = 0
        };

        var result = await InvokeAsync(WalletAddress, request);

        // HTTPS passed the URL guard — the result must NOT be the guard's BadRequest.
        // (It is now a fail-closed Problem result because no issuance key is configured.)
        result.GetType().Name.Should().NotContain("BadRequest");
    }

    private async Task<IResult> InvokeAsync(string walletAddress, IssueCredentialRequest request)
    {
        var method = typeof(CredentialEndpoints).GetMethod(
            "IssueCredential",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("IssueCredential handler should exist");

        var result = method.Invoke(null, [
            walletAddress,
            request,
            _walletRepository.Object,
            null!, // IKeyManagementService — not reached by the URL guard
            null!, // ISdJwtService
            null!, // ICredentialStore
            new NullLoggerFactory(),
            null!, // IWalletInboxWriter — not reached by the URL guard
            null,  // IIssuanceKeyService (optional — F120)
            null,  // IOrgCertChainProvider (optional)
            null,  // IDeviceBoundCopyIssuanceCoordinator (optional — F1195 Phase 2)
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }
}
