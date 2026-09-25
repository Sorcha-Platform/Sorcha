// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Cryptography.SdJwt;
using Sorcha.ServiceClients.Trust;
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
/// #1699 — issuance never attaches an x5c chain the credential does not verify under. Drives the
/// real handler with the REAL <see cref="SdJwtService"/> and real keys: the live defect was a
/// perfectly valid Ed25519 signature carrying a P-256 certificate chain, so a mocked signer could
/// not see it.
/// </summary>
public sealed class IssueCredentialX5cKeyMatchTests
{
    private const string WalletAddress = "ws1qissuer1";

    private readonly Mock<IWalletRepository> _walletRepository = new();
    private readonly SdJwtService _sdJwt = new();
    private readonly Mock<ICredentialStore> _store = new();
    private readonly Mock<IIssuanceKeyService> _issuanceKey = new();
    private readonly Mock<IWalletInboxWriter> _inboxWriter = new();
    private readonly Mock<IOrgCertChainProvider> _chains = new();
    private readonly List<CredentialEntity> _stored = new();

    public IssueCredentialX5cKeyMatchTests()
    {
        _walletRepository.Setup(r => r.GetByAddressAsync(
                WalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletEntity
            {
                Address = WalletAddress, EncryptedPrivateKey = "k", EncryptionKeyId = "kid",
                Algorithm = "ED25519", Owner = "owner", Tenant = "tenant", Name = "w",
                Status = WalletStatus.Active
            });
        _issuanceKey.Setup(s => s.GetOrDeriveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuanceKeyState?)null);
        _store.Setup(s => s.StoreAsync(It.IsAny<CredentialEntity>(), It.IsAny<CancellationToken>()))
            .Callback<CredentialEntity, CancellationToken>((e, _) => _stored.Add(e))
            .Returns(Task.CompletedTask);
        _inboxWriter.Setup(w => w.WriteCredentialReceivedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>The live #1699 shape, default anchor: issue, but without the misleading chain.</summary>
    [Fact]
    public async Task Ed25519IssuanceKeyWithP256OrgCert_DefaultAnchor_IssuesWithoutX5c_AndStillVerifies()
    {
        var ed25519 = Sodium.PublicKeyAuth.GenerateKeyPair();
        SigningKeyIs(ed25519.PrivateKey, "ED25519");
        using var certKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        OrgCertIsFor(certKey);

        var result = await InvokeAsync(Request(trustAnchor: null));

        result.GetType().Name.Should().Contain("Ok");
        var token = _stored.Should().ContainSingle().Subject.RawToken!;
        HeaderHas(token, "x5c").Should().BeFalse("the chain certifies a key that did not sign this credential");
        (await _sdJwt.VerifyPresentationAsync(token, ed25519.PublicKey, "EdDSA")).IsValid.Should().BeTrue();
    }

    /// <summary>An explicit X.509 anchor cannot be honoured without a matching chain — refuse.</summary>
    [Theory]
    [InlineData("x509-tenant")]
    public async Task Ed25519IssuanceKeyWithP256OrgCert_X509Anchor_RefusesWithCertKeyMismatch(string anchor)
    {
        var ed25519 = Sodium.PublicKeyAuth.GenerateKeyPair();
        SigningKeyIs(ed25519.PrivateKey, "ED25519");
        using var certKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        OrgCertIsFor(certKey);

        var result = await InvokeAsync(Request(trustAnchor: anchor));

        var status = (IStatusCodeHttpResult)result;
        status.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        JsonSerializer.Serialize(((IValueHttpResult)result).Value).Should().Contain("CERT_KEY_MISMATCH");
        _stored.Should().BeEmpty("nothing is minted when the requested anchor cannot be honoured");
    }

    /// <summary>When the org certificate DOES certify the signing key, the chain is kept.</summary>
    [Fact]
    public async Task SigningKeyCertifiedByOrgCert_KeepsX5c()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SigningKeyIs(key.ExportECPrivateKey(), "ES256");
        OrgCertIsFor(key);

        var result = await InvokeAsync(Request(trustAnchor: "x509-tenant"));

        result.GetType().Name.Should().Contain("Ok");
        HeaderHas(_stored.Should().ContainSingle().Subject.RawToken!, "x5c").Should().BeTrue();
    }

    private void SigningKeyIs(byte[] privateKey, string algorithm) =>
        _issuanceKey.Setup(s => s.GetActiveSigningMaterialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orgId, CancellationToken _) => new IssuanceSigningMaterial(
                orgId, "did:sorcha:org:ws1qissuer1", "did:sorcha:org:ws1qissuer1#vc-issuance-1",
                privateKey, algorithm, 1));

    private void OrgCertIsFor(ECDsa leafKey)
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var rootReq = new CertificateRequest("CN=Root", rootKey, HashAlgorithmName.SHA256);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
        using var root = rootReq.CreateSelfSigned(now, now.AddYears(5));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var leaf = new CertificateRequest("CN=Org", leafKey, HashAlgorithmName.SHA256)
            .Create(root.SubjectName, X509SignatureGenerator.CreateForECDsa(rootKey), now, now.AddYears(2), serial);

        _chains.Setup(c => c.GetChainForAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgCertChain(leaf.RawData, root.RawData));
    }

    private static IssueCredentialRequest Request(string? trustAnchor) => new()
    {
        CredentialType = "CyberEssentialsUac",
        Claims = new Dictionary<string, object> { ["compliant"] = true },
        RecipientWallet = "ws1qrecipient1",
        SkipRecipientStore = true,
        TenantId = Guid.NewGuid().ToString(),
        TrustAnchor = trustAnchor,
    };

    private static bool HeaderHas(string token, string property)
    {
        var header = Base64Url.DecodeFromChars(token.Split('~')[0].Split('.')[0]);
        using var doc = JsonDocument.Parse(header);
        return doc.RootElement.TryGetProperty(property, out _);
    }

    private async Task<IResult> InvokeAsync(IssueCredentialRequest request)
    {
        var method = typeof(CredentialEndpoints).GetMethod(
            "IssueCredential", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = method.Invoke(null, [
            WalletAddress, request, _walletRepository.Object,
            null!, // IKeyManagementService — not on the issuance-key path
            _sdJwt, _store.Object, new NullLoggerFactory(), _inboxWriter.Object,
            _issuanceKey.Object, _chains.Object,
            null,  // IDeviceBoundCopyIssuanceCoordinator
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }
}
