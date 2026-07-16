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
/// Mint-path wiring for the device-bound copy policy (Feature 1195, Phase 2, Task 5).
/// Asserts that <c>IssueCredential</c> consults the
/// <see cref="IDeviceBoundCopyIssuanceCoordinator"/> for a holder-bound (cnf) issuance,
/// embeds the wallet-owned F114 status slot the coordinator returns, and aborts the mint
/// (no credential stored) when the coordinator throws. Reflection-based static-handler
/// invocation per <see cref="IssueCredentialVctDecouplingTests"/>.
/// </summary>
public sealed class IssueCredentialDeviceBoundTests
{
    private const string WalletAddress = "ws1qissuer1";
    private const string Recipient = "ws1qcitizen1";
    private const string VctUri = "https://credentials.sorcha.dev/assured-identity";

    private static readonly JsonElement DeviceJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"EC","crv":"P-256","x":"device-x-value-0000000000000000000000000000","y":"device-y-value-0000000000000000000000000000"}""");

    private readonly Mock<IWalletRepository> _walletRepository = new();
    private readonly Mock<ISdJwtService> _sdJwt = new();
    private readonly Mock<ICredentialStore> _store = new();
    private readonly Mock<IIssuanceKeyService> _issuanceKey = new();
    private readonly Mock<IWalletInboxWriter> _inboxWriter = new();
    private readonly Mock<IDeviceBoundCopyIssuanceCoordinator> _coordinator = new();
    private readonly List<CredentialEntity> _stored = new();
    private Dictionary<string, object>? _signedClaims;

    public IssueCredentialDeviceBoundTests()
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
        // Recipient wallet absent → only the issuer copy is stored (SkipRecipientStore also set).
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

        // Capture the claims the wallet signs (holder-jwk overload — cnf binding present).
        _sdJwt.Setup(s => s.CreateTokenAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<JsonElement>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyList<byte[]>?>(),
                It.IsAny<string?>()))
            .Callback((Dictionary<string, object> claims, IEnumerable<string>? _, string _, string _, byte[] _, string _,
                    JsonElement _, DateTimeOffset? _, CancellationToken _, IReadOnlyList<byte[]>? _, string? _) =>
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

    private IssueCredentialRequest DeviceBoundRequest() => new()
    {
        CredentialType = "AssuredIdentityCredential",
        Vct = VctUri,
        Claims = new Dictionary<string, object> { ["foo"] = "bar" },
        RecipientWallet = Recipient,
        SkipRecipientStore = true,
        TenantId = Guid.NewGuid().ToString(),
        HolderJwk = DeviceJwk,
    };

    [Fact]
    public async Task IssueCredential_DeviceBoundPlan_EmbedsIetfStatusAndStoresF114Slot()
    {
        var statusUrl = $"https://n1.sorcha.dev/api/v1/wallet/status/{Guid.NewGuid():N}/citizen-devices/0.statuslist+jwt";
        _coordinator.Setup(c => c.PrepareAsync(
                Recipient, VctUri, It.IsAny<JsonElement>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceBoundMintPlan(statusUrl, 7));

        var result = await InvokeAsync(WalletAddress, DeviceBoundRequest());

        result.GetType().Name.Should().Contain("Ok");
        var issuer = _stored.Should().ContainSingle().Subject;
        issuer.StatusListUrl.Should().Be(statusUrl, "the device copy embeds the wallet-owned F114 slot");
        issuer.StatusListIndex.Should().Be(7);

        _signedClaims.Should().NotBeNull();
        _signedClaims!.Should().ContainKey("status", "device copies use the IETF Token Status List claim shape");
        _coordinator.Verify(c => c.PrepareAsync(
            Recipient, VctUri, It.IsAny<JsonElement>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueCredential_CoordinatorReturnsNull_LeavesStatusUnchanged()
    {
        // Web root / non-device: coordinator returns null → no status slot embedded.
        _coordinator.Setup(c => c.PrepareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceBoundMintPlan?)null);

        await InvokeAsync(WalletAddress, DeviceBoundRequest());

        var issuer = _stored.Should().ContainSingle().Subject;
        issuer.StatusListUrl.Should().BeNull("a null plan leaves the request's (absent) status allocation unchanged");
        issuer.StatusListIndex.Should().BeNull();
        _signedClaims.Should().NotBeNull();
        _signedClaims!.Should().NotContainKey("status");
    }

    [Fact]
    public async Task IssueCredential_CoordinatorThrows_AbortsMintAndStoresNothing()
    {
        _coordinator.Setup(c => c.PrepareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("eviction revoke failed"));

        var result = await InvokeAsync(WalletAddress, DeviceBoundRequest());

        result.GetType().Name.Should().NotContain("Ok", "a policy abort must not mint a credential");
        _stored.Should().BeEmpty("no credential is signed or stored when the policy aborts");
        _sdJwt.Verify(s => s.CreateTokenAsync(
                It.IsAny<Dictionary<string, object>>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<JsonElement>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<byte[]>?>(), It.IsAny<string?>()),
            Times.Never);
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
