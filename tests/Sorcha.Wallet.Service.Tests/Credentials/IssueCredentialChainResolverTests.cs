// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;

using Microsoft.Extensions.Logging.Abstractions;

using FluentAssertions;
using Moq;

using Sorcha.ServiceClients.Trust;
using Sorcha.Wallet.Service.Credentials;

using Xunit;

namespace Sorcha.Wallet.Service.Tests.Credentials;

/// <summary>
/// Feature 096 US3 — pins the resolution rules between the IssueCredential endpoint
/// and the Tenant Service trust client. The endpoint must:
///   • skip the chain fetch entirely when no provider is registered (dev / opt-out),
///   • skip when the caller (Blueprint Service) didn't supply a tenant id,
///   • return null (no x5c) when the Tenant Service has no enrolment,
///   • return leaf-first chain bytes ready for the JWS x5c header on success,
///   • absorb provider exceptions so a Tenant Service hiccup never breaks issuance.
/// </summary>
public class IssueCredentialChainResolverTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string IssuerWallet = "ws1qissuer";

    [Fact]
    public async Task ResolveChainAsync_NullProvider_ReturnsNull()
    {
        var result = await IssueCredentialChainResolver.ResolveChainAsync(
            provider: null,
            tenantId: TenantId,
            issuerWallet: IssuerWallet,
            logger: NullLogger.Instance,
            cancellationToken: default);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveChainAsync_BlankTenantId_ReturnsNull_WithoutCallingProvider(string? tenantId)
    {
        var provider = new Mock<IOrgCertChainProvider>(MockBehavior.Strict);

        var result = await IssueCredentialChainResolver.ResolveChainAsync(
            provider: provider.Object,
            tenantId: tenantId,
            issuerWallet: IssuerWallet,
            logger: NullLogger.Instance,
            cancellationToken: default);

        result.Should().BeNull();
        provider.Verify(p => p.GetChainForAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveChainAsync_ProviderReturnsNull_ReturnsNull()
    {
        var provider = new Mock<IOrgCertChainProvider>();
        provider.Setup(p => p.GetChainForAsync(TenantId, IssuerWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrgCertChain?)null);

        var result = await IssueCredentialChainResolver.ResolveChainAsync(
            provider: provider.Object,
            tenantId: TenantId,
            issuerWallet: IssuerWallet,
            logger: NullLogger.Instance,
            cancellationToken: default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveChainAsync_ProviderReturnsChain_ReturnsLeafThenRoot()
    {
        var leafDer = new byte[] { 0x30, 0x82, 0x01, 0x01 };
        var rootDer = new byte[] { 0x30, 0x82, 0x02, 0x02 };
        var provider = new Mock<IOrgCertChainProvider>();
        provider.Setup(p => p.GetChainForAsync(TenantId, IssuerWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgCertChain(leafDer, rootDer));

        var result = await IssueCredentialChainResolver.ResolveChainAsync(
            provider: provider.Object,
            tenantId: TenantId,
            issuerWallet: IssuerWallet,
            logger: NullLogger.Instance,
            cancellationToken: default);

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].Should().BeEquivalentTo(leafDer);
        result[1].Should().BeEquivalentTo(rootDer);
    }

    [Fact]
    public async Task ResolveChainAsync_ProviderThrows_ReturnsNull_NoRethrow()
    {
        var provider = new Mock<IOrgCertChainProvider>();
        provider.Setup(p => p.GetChainForAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("tenant down"));

        var result = await IssueCredentialChainResolver.ResolveChainAsync(
            provider: provider.Object,
            tenantId: TenantId,
            issuerWallet: IssuerWallet,
            logger: NullLogger.Instance,
            cancellationToken: default);

        result.Should().BeNull();
    }
}
