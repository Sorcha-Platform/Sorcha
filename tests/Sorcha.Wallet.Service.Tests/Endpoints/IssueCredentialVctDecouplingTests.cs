// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

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
/// Credential VCT decoupling — the live issuance endpoint must stamp the stored
/// <see cref="CredentialEntity.Type"/> with the canonical <c>vct</c> URI (not the bare
/// <c>CredentialType</c>) and record the authored display name as <c>credentialName</c>
/// in <see cref="CredentialEntity.DisplayConfigJson"/>. Because the SorchaLocalWallet
/// register envelope copies the issuer response <c>Type</c> into <c>credentialType</c>,
/// this is what propagates the URI all the way to the citizen entity.
///
/// Reflection-based static-handler invocation per the established pattern in
/// <see cref="IssueCredentialStatusListUrlGuardTests"/>. This test drives the full store
/// path by wiring a valid issuance key + a stub SD-JWT signer, so the guard's 409
/// fail-closed (no issuance key) is not reached.
/// </summary>
public sealed class IssueCredentialVctDecouplingTests
{
    private const string WalletAddress = "ws1qissuer1";
    private const string VctUri = "https://credentials.sorcha.dev/assured-identity";

    private readonly Mock<IWalletRepository> _walletRepository = new();
    private readonly Mock<ISdJwtService> _sdJwt = new();
    private readonly Mock<ICredentialStore> _store = new();
    private readonly Mock<IIssuanceKeyService> _issuanceKey = new();
    private readonly Mock<IWalletInboxWriter> _inboxWriter = new();
    private readonly List<CredentialEntity> _stored = new();

    public IssueCredentialVctDecouplingTests()
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
            .ReturnsAsync(new SdJwtToken { RawToken = "eyJhbGciOiJFZERTQSJ9.test.sig~" });

        _store.Setup(s => s.StoreAsync(It.IsAny<CredentialEntity>(), It.IsAny<CancellationToken>()))
            .Callback<CredentialEntity, CancellationToken>((e, _) => _stored.Add(e))
            .Returns(Task.CompletedTask);

        _inboxWriter.Setup(w => w.WriteCredentialReceivedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task IssueCredential_WithVct_StoresIssuerEntityTypeAsVctUri()
    {
        var request = new IssueCredentialRequest
        {
            CredentialType = "AssuredIdentityCredential",
            Vct = VctUri,
            Claims = new Dictionary<string, object> { ["foo"] = "bar" },
            RecipientWallet = "ws1qrecipient1",
            SkipRecipientStore = true,
            TenantId = Guid.NewGuid().ToString()
        };

        var result = await InvokeAsync(WalletAddress, request);

        result.GetType().Name.Should().Contain("Ok");
        var issuer = _stored.Should().ContainSingle().Subject;
        issuer.Type.Should().Be(VctUri,
            "the citizen entity's Type is stamped from the vct URI via the response → envelope → detector chain");
    }

    [Fact]
    public async Task IssueCredential_WithDisplayName_StoresDisplayConfigJsonWithCredentialName()
    {
        var request = new IssueCredentialRequest
        {
            CredentialType = "AssuredIdentityCredential",
            Vct = VctUri,
            DisplayName = "Assured Identity",
            Claims = new Dictionary<string, object> { ["foo"] = "bar" },
            RecipientWallet = "ws1qrecipient1",
            SkipRecipientStore = true,
            TenantId = Guid.NewGuid().ToString()
        };

        await InvokeAsync(WalletAddress, request);

        var issuer = _stored.Should().ContainSingle().Subject;
        issuer.DisplayConfigJson.Should().NotBeNull();
        issuer.DisplayConfigJson.Should().Contain("credentialName");
        issuer.DisplayConfigJson.Should().Contain("Assured Identity");
    }

    [Fact]
    public async Task IssueCredential_WithoutVct_FallsBackToCredentialTypeAndNullDisplayConfig()
    {
        var request = new IssueCredentialRequest
        {
            CredentialType = "AssuredIdentityCredential",
            Claims = new Dictionary<string, object> { ["foo"] = "bar" },
            RecipientWallet = "ws1qrecipient1",
            SkipRecipientStore = true,
            TenantId = Guid.NewGuid().ToString()
        };

        await InvokeAsync(WalletAddress, request);

        var issuer = _stored.Should().ContainSingle().Subject;
        issuer.Type.Should().Be("AssuredIdentityCredential",
            "no vct supplied → the bare CredentialType remains the defensive fallback");
        issuer.DisplayConfigJson.Should().BeNull("no display name supplied → no display config");
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
            null!, // IKeyManagementService — not used on the issuance-key signing path (Feature 149)
            _sdJwt.Object,
            _store.Object,
            new NullLoggerFactory(),
            _inboxWriter.Object,
            _issuanceKey.Object,
            null,  // IOrgCertChainProvider (optional)
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }
}
