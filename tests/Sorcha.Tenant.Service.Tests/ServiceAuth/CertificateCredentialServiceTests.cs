// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Models.Auth;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Tenant.Service.Tests.ServiceAuth;

/// <summary>
/// F191 US1 — unit tests for the certificate-credential mint path in <see cref="ServiceAuthService"/>.
/// The certificate handed in here is assumed chain-validated at the TLS layer (Kestrel
/// RequireCertificate + workload trust bundle); these tests cover the identity-matching and
/// principal gating that happen after the handshake.
/// </summary>
public class CertificateCredentialServiceTests
{
    private const string Installation = "sorcha";

    private readonly Mock<IIdentityRepository> _identityRepository = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<ILogger<ServiceAuthService>> _logger = new();
    private readonly X509Certificate2 _workloadCa;

    public CertificateCredentialServiceTests()
    {
        _workloadCa = WorkloadCertificateAuthority.CreateCertificateAuthority(Installation);
        _tokenService
            .Setup(t => t.GenerateServiceTokenAsync(
                It.IsAny<ServicePrincipal>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResponse { AccessToken = "token", TokenType = "Bearer" });
    }

    private ServiceAuthService CreateSut(string installationName = Installation)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:InstallationName"] = installationName,
            })
            .Build();

        return new ServiceAuthService(
            _identityRepository.Object,
            _tokenService.Object,
            configuration,
            _logger.Object);
    }

    private X509Certificate2 IssueLeaf(string clientId, string installation = Installation) =>
        WorkloadCertificateAuthority.IssueServiceCertificate(
            _workloadCa, SpiffeId.ForService(installation, clientId), "test-host");

    private static ServicePrincipal Principal(
        string clientId,
        ServicePrincipalStatus status = ServicePrincipalStatus.Active,
        string[]? scopes = null) => new()
        {
            Id = Guid.NewGuid(),
            ServiceName = clientId,
            ClientId = clientId,
            ClientSecretEncrypted = new byte[48],
            Scopes = scopes ?? ["blueprints:read", "wallets:sign"],
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void RepositoryReturns(ServicePrincipal? principal, string clientId) =>
        _identityRepository
            .Setup(r => r.GetServicePrincipalByClientIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(principal);

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_MatchingIdentity_Mints()
    {
        var principal = Principal("service-blueprint");
        RepositoryReturns(principal, "service-blueprint");
        using var leaf = IssueLeaf("service-blueprint");

        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "service-blueprint", leaf, requestedScopes: null);

        result.Should().NotBeNull();
        _tokenService.Verify(t => t.GenerateServiceTokenAsync(principal, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_SanClientIdMismatch_RefusesAndLogsBothIdentities()
    {
        RepositoryReturns(Principal("service-wallet"), "service-wallet");
        using var blueprintLeaf = IssueLeaf("service-blueprint");

        // service-wallet is requested, but the presented certificate names service-blueprint.
        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "service-wallet", blueprintLeaf, requestedScopes: null);

        result.Should().BeNull();
        _tokenService.Verify(t => t.GenerateServiceTokenAsync(
            It.IsAny<ServicePrincipal>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);

        // The refusal log must carry BOTH identities so operators can see what was presented vs asked.
        _logger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("service-blueprint") && state.ToString()!.Contains("service-wallet")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_WrongInstallation_Refuses()
    {
        RepositoryReturns(Principal("service-blueprint"), "service-blueprint");
        using var foreignLeaf = IssueLeaf("service-blueprint", installation: "other-install");

        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "service-blueprint", foreignLeaf, requestedScopes: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_CertificateWithoutSpiffeSan_Refuses()
    {
        RepositoryReturns(Principal("tenant-service"), "tenant-service");
        // A server certificate has a DNS SAN only — no workload identity to match.
        using var serverCert = WorkloadCertificateAuthority.IssueServerCertificate(_workloadCa, "tenant-service");

        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "tenant-service", serverCert, requestedScopes: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_UnknownPrincipal_Refuses()
    {
        RepositoryReturns(null, "service-blueprint");
        using var leaf = IssueLeaf("service-blueprint");

        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "service-blueprint", leaf, requestedScopes: null);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(ServicePrincipalStatus.Suspended)]
    [InlineData(ServicePrincipalStatus.Revoked)]
    public async Task AuthenticateServiceWithCertificateAsync_NonActivePrincipal_Refuses(ServicePrincipalStatus status)
    {
        RepositoryReturns(Principal("service-blueprint", status), "service-blueprint");
        using var leaf = IssueLeaf("service-blueprint");

        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "service-blueprint", leaf, requestedScopes: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_ScopeIntersection_MatchesSecretPathSemantics()
    {
        var principal = Principal("service-blueprint", scopes: ["blueprints:read", "wallets:sign"]);
        RepositoryReturns(principal, "service-blueprint");
        using var leaf = IssueLeaf("service-blueprint");

        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "service-blueprint", leaf, requestedScopes: "wallets:sign something:else");

        result.Should().NotBeNull();
        // Same in-place filtering the secret path performs: only the intersection is minted.
        _tokenService.Verify(t => t.GenerateServiceTokenAsync(
            It.Is<ServicePrincipal>(p => p.Scopes.SequenceEqual(new[] { "wallets:sign" })),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_EmptyScopeIntersection_Refuses()
    {
        RepositoryReturns(Principal("service-blueprint", scopes: ["blueprints:read"]), "service-blueprint");
        using var leaf = IssueLeaf("service-blueprint");

        var result = await CreateSut().AuthenticateServiceWithCertificateAsync(
            "service-blueprint", leaf, requestedScopes: "wallets:sign");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateServiceWithCertificateAsync_DefaultInstallation_UsedWhenUnconfigured()
    {
        // No JwtSettings:InstallationName ⇒ SorchaAudiences.DefaultInstallation ("sorcha") —
        // the SPIFFE trust domain must come from the SAME source as the JWT audience namespace.
        var principal = Principal("service-blueprint");
        RepositoryReturns(principal, "service-blueprint");
        using var leaf = IssueLeaf("service-blueprint", installation: "sorcha");

        var configuration = new ConfigurationBuilder().Build();
        var sut = new ServiceAuthService(
            _identityRepository.Object, _tokenService.Object, configuration, _logger.Object);

        var result = await sut.AuthenticateServiceWithCertificateAsync("service-blueprint", leaf, null);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthenticateWithDelegationByCertificateAsync_MatchingIdentityAndDelegateScope_Mints()
    {
        var userId = Guid.NewGuid();
        var principal = Principal("tenant-service", scopes: ["tenant:delegate", "wallets:sign"]);
        RepositoryReturns(principal, "tenant-service");
        using var leaf = IssueLeaf("tenant-service");

        var result = await CreateSut().AuthenticateWithDelegationByCertificateAsync(
            "tenant-service", leaf, userId, delegatedOrgId: null, requestedScopes: null);

        result.Should().NotBeNull();
        _tokenService.Verify(t => t.GenerateServiceTokenAsync(principal, userId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateWithDelegationByCertificateAsync_WithoutDelegateScope_Refuses()
    {
        RepositoryReturns(Principal("service-blueprint"), "service-blueprint");
        using var leaf = IssueLeaf("service-blueprint");

        var result = await CreateSut().AuthenticateWithDelegationByCertificateAsync(
            "service-blueprint", leaf, Guid.NewGuid(), null, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateWithDelegationByCertificateAsync_SanMismatch_Refuses()
    {
        RepositoryReturns(Principal("tenant-service", scopes: ["tenant:delegate"]), "tenant-service");
        using var blueprintLeaf = IssueLeaf("service-blueprint");

        var result = await CreateSut().AuthenticateWithDelegationByCertificateAsync(
            "tenant-service", blueprintLeaf, Guid.NewGuid(), null, null);

        result.Should().BeNull();
    }
}
