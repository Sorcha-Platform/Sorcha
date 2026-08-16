// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Issue #1447. Two seams on <c>IssueCredential</c>'s status-list handling:
/// (1) the pre-sign validation must refuse the reserved <c>sorcha.example</c> placeholder —
/// a status URL is signed into the credential and permanently broken after; and
/// (2) a request-carried status URL + index must actually surface as the W3C
/// <c>credentialStatus</c> claim in the signed payload (the n1 evidence suggested
/// credentials with wallet rows populated but no status claim — this pins the claim path).
/// </summary>
public sealed class IssueCredentialStatusUrlTests
{
    private const string WalletAddress = "ws1qissuer1";
    private const string Recipient = "ws1qcitizen1";

    private readonly Mock<IWalletRepository> _walletRepository = new();
    private readonly Mock<ISdJwtService> _sdJwt = new();
    private readonly Mock<ICredentialStore> _store = new();
    private readonly Mock<IIssuanceKeyService> _issuanceKey = new();
    private readonly Mock<IWalletInboxWriter> _inboxWriter = new();
    private readonly Mock<IDeviceBoundCopyIssuanceCoordinator> _coordinator = new();
    private readonly List<CredentialEntity> _stored = new();
    private Dictionary<string, object>? _signedClaims;

    public IssueCredentialStatusUrlTests()
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
        _walletRepository.Setup(r => r.GetByAddressAsync(
                Recipient, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        _issuanceKey.Setup(s => s.GetOrDeriveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuanceKeyState?)null);
        _issuanceKey.Setup(s => s.GetActiveSigningMaterialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orgId, CancellationToken _) => new IssuanceSigningMaterial(
                OrganizationId: orgId,
                IssuerDid: "did:sorcha:org:ws1qissuer1",
                Kid: "did:sorcha:org:ws1qissuer1#vc-issuance-1",
                PrivateKey: new byte[32],
                Algorithm: "ED25519",
                RotationIndex: 1));

        _coordinator.Setup(c => c.PrepareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceBoundMintPlan?)null);

        // Capture the claims the wallet signs (no-holder-jwk overload — plain issuance).
        _sdJwt.Setup(s => s.CreateTokenAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyList<byte[]>?>(),
                It.IsAny<string?>()))
            .Callback((Dictionary<string, object> claims, IEnumerable<string>? _, string _, string _, byte[] _,
                    string _, DateTimeOffset? _, CancellationToken _, IReadOnlyList<byte[]>? _, string? _) =>
                _signedClaims = claims)
            .ReturnsAsync(new SdJwtToken { RawToken = "eyJhbGciOiJFZERTQSJ9.test.sig~" });

        _store.Setup(s => s.StoreAsync(It.IsAny<CredentialEntity>(), It.IsAny<CancellationToken>()))
            .Callback<CredentialEntity, CancellationToken>((e, _) => _stored.Add(e))
            .Returns(Task.CompletedTask);

        _inboxWriter.Setup(w => w.WriteCredentialReceivedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static IssueCredentialRequest Request(string? statusUrl, int? statusIndex) => new()
    {
        CredentialType = "CyberEssentialsUacCredential",
        Claims = new Dictionary<string, object> { ["level"] = "silver" },
        RecipientWallet = Recipient,
        SkipRecipientStore = true,
        TenantId = Guid.NewGuid().ToString(),
        StatusListUrl = statusUrl,
        StatusListIndex = statusIndex,
        StatusListPurpose = statusUrl is null ? null : "revocation",
    };

    [Fact]
    public async Task IssueCredential_PlaceholderStatusUrl_IsRefusedBeforeSigning()
    {
        var result = await InvokeAsync(WalletAddress, Request(
            "https://sorcha.example/api/v1/credentials/status-lists/ws1qissuer1-r1-revocation-1", 3));

        result.GetType().Name.Should().Contain("BadRequest",
            "a sorcha.example status URL can never be dereferenced and must be refused before it is signed into the credential");
        _stored.Should().BeEmpty();
        _sdJwt.Verify(s => s.CreateTokenAsync(
                It.IsAny<Dictionary<string, object>>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyList<byte[]>?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task IssueCredential_RequestStatusUrl_EmbedsW3CCredentialStatusClaim()
    {
        var url = "https://n1.sorcha.dev/api/v1/credentials/status-lists/ws1qissuer1-r1-revocation-1";

        var result = await InvokeAsync(WalletAddress, Request(url, 3));

        result.GetType().Name.Should().Contain("Ok");
        _signedClaims.Should().NotBeNull();
        _signedClaims!.Should().ContainKey("credentialStatus",
            "the request-carried status allocation must reach the signed payload — a wallet row alone gives a verifier nothing to dereference");

        var status = _signedClaims["credentialStatus"].Should()
            .BeOfType<Dictionary<string, object>>().Subject;
        status["statusListCredential"].Should().Be(url);
        status["statusListIndex"].Should().Be("3");
        status["statusPurpose"].Should().Be("revocation");
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
            null!, // IKeyManagementService — not used on the issuance-key signing path
            _sdJwt.Object,
            _store.Object,
            new NullLoggerFactory(),
            _inboxWriter.Object,
            _issuanceKey.Object,
            null,  // IOrgCertChainProvider (optional)
            _coordinator.Object,
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }
}
