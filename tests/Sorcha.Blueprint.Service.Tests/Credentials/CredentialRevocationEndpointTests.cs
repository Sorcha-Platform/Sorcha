// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Services;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Tests.Credentials;

public class CredentialRevocationEndpointTests
{
    private readonly Mock<IWalletServiceClient> _walletClientMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();
    private readonly Mock<IStatusListManager> _statusListManagerMock = new();
    private readonly Mock<ILoggerFactory> _loggerFactoryMock = new();

    public CredentialRevocationEndpointTests()
    {
        var loggerMock = new Mock<ILogger>();
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(loggerMock.Object);
    }

    [Fact]
    public async Task RevokeCredential_ValidIssuer_ReturnsOkWithRevocationDetails()
    {
        // Arrange
        var credentialId = "urn:uuid:test-credential-1";
        var issuerWallet = "wallet-issuer-001";

        _walletClientMock
            .Setup(w => w.GetCredentialAsync(issuerWallet, credentialId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialIssuanceResult
            {
                CredentialId = credentialId,
                Type = "LicenseCredential",
                IssuerDid = issuerWallet,
                SubjectDid = "wallet-recipient-001",
                Claims = new Dictionary<string, object> { ["type"] = "LicenseCredential" },
                IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
                RawToken = "dummy.token.value"
            });

        _walletClientMock
            .Setup(w => w.UpdateCredentialStatusAsync(issuerWallet, credentialId, "Revoked", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _walletClientMock
            .Setup(w => w.UpdateCredentialStatusAsync("wallet-recipient-001", credentialId, "Revoked", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await InvokeRevokeEndpoint(credentialId, new RevokeCredentialRequest
        {
            IssuerWallet = issuerWallet,
            Reason = "License expired"
        });

        // Assert
        var okResult = result.Should().BeOfType<Ok<RevokeCredentialResponse>>().Subject;
        okResult.Value!.CredentialId.Should().Be(credentialId);
        okResult.Value.RevokedBy.Should().Be(issuerWallet);
        okResult.Value.Status.Should().Be("Revoked");
        okResult.Value.Reason.Should().Be("License expired");

        _walletClientMock.Verify(
            w => w.UpdateCredentialStatusAsync(issuerWallet, credentialId, "Revoked", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeCredential_NonIssuer_ReturnsForbid()
    {
        // Arrange
        var credentialId = "urn:uuid:test-credential-2";
        var callerWallet = "wallet-not-issuer";
        var actualIssuer = "wallet-real-issuer";

        _walletClientMock
            .Setup(w => w.GetCredentialAsync(callerWallet, credentialId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialIssuanceResult
            {
                CredentialId = credentialId,
                Type = "LicenseCredential",
                IssuerDid = actualIssuer,
                SubjectDid = callerWallet,
                Claims = new Dictionary<string, object> { ["type"] = "LicenseCredential" },
                IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
                RawToken = "dummy.token.value"
            });

        // Act
        var result = await InvokeRevokeEndpoint(credentialId, new RevokeCredentialRequest
        {
            IssuerWallet = callerWallet,
            Reason = "Trying to revoke someone else's credential"
        });

        // Assert
        result.Should().BeOfType<ForbidHttpResult>();
        _walletClientMock.Verify(
            w => w.UpdateCredentialStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeCredential_AlreadyRevoked_ReturnsOkIdempotent()
    {
        // Arrange
        var credentialId = "urn:uuid:test-credential-3";
        var issuerWallet = "wallet-issuer-003";

        _walletClientMock
            .Setup(w => w.GetCredentialAsync(issuerWallet, credentialId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialIssuanceResult
            {
                CredentialId = credentialId,
                Type = "LicenseCredential",
                IssuerDid = issuerWallet,
                SubjectDid = "wallet-recipient-003",
                Claims = new Dictionary<string, object> { ["type"] = "LicenseCredential" },
                IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
                RawToken = "dummy.token.value"
            });

        // UpdateCredentialStatusAsync returns false — credential was already revoked
        _walletClientMock
            .Setup(w => w.UpdateCredentialStatusAsync(issuerWallet, credentialId, "Revoked", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await InvokeRevokeEndpoint(credentialId, new RevokeCredentialRequest
        {
            IssuerWallet = issuerWallet,
            Reason = "Duplicate revocation"
        });

        // Assert — should still return Ok (idempotent)
        var okResult = result.Should().BeOfType<Ok<RevokeCredentialResponse>>().Subject;
        okResult.Value!.Status.Should().Be("Revoked");
    }

    [Fact]
    public async Task RevokeCredential_CredentialNotFound_ReturnsNotFound()
    {
        // Arrange
        var credentialId = "urn:uuid:nonexistent";
        var issuerWallet = "wallet-issuer-004";

        _walletClientMock
            .Setup(w => w.GetCredentialAsync(issuerWallet, credentialId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialIssuanceResult?)null);

        // Act
        var result = await InvokeRevokeEndpoint(credentialId, new RevokeCredentialRequest
        {
            IssuerWallet = issuerWallet
        });

        // Assert — endpoint returns Results.NotFound(new { error = ... })
        result.GetType().Name.Should().Contain("NotFound");
    }

    /// <summary>
    /// Helper to invoke the static endpoint method directly (bypasses HTTP pipeline).
    /// Uses reflection since the endpoint handler is private static.
    /// </summary>
    private async Task<IResult> InvokeRevokeEndpoint(string credentialId, RevokeCredentialRequest request)
    {
        // Use reflection to access the private static method
        var method = typeof(CredentialEndpoints).GetMethod(
            "RevokeCredential",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("RevokeCredential method should exist");

        var result = method!.Invoke(null, [
            credentialId,
            request,
            _walletClientMock.Object,
            _registerClientMock.Object,
            _statusListManagerMock.Object,
            _loggerFactoryMock.Object,
            CancellationToken.None
        ]);

        return await (Task<IResult>)result!;
    }
}
