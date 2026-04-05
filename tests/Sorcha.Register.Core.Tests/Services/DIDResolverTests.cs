// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;
using Xunit;

namespace Sorcha.Register.Core.Tests.Services;

public class DIDResolverTests
{
    private readonly Mock<IWalletServiceClient> _walletClientMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly DIDResolver _resolver;

    public DIDResolverTests()
    {
        _walletClientMock = new Mock<IWalletServiceClient>();
        _registerClientMock = new Mock<IRegisterServiceClient>();
        var logger = new Mock<ILogger<DIDResolver>>();
        _resolver = new DIDResolver(_walletClientMock.Object, _registerClientMock.Object, logger.Object);
    }

    // --- Wallet DID Resolution ---

    [Fact]
    public async Task ResolveAsync_ValidWalletDid_ReturnsPublicKey()
    {
        var walletAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
        _walletClientMock
            .Setup(c => c.GetWalletAsync(walletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = walletAddress,
                Name = "Test Wallet",
                PublicKey = "base64pubkey==",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "user1",
                Tenant = "tenant1"
            });

        var result = await _resolver.ResolveAsync($"did:sorcha:w:{walletAddress}");

        result.IsResolved.Should().BeTrue();
        result.PublicKey.Should().Be("base64pubkey==");
        result.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task ResolveAsync_UnknownWalletAddress_ReturnsFailure()
    {
        _walletClientMock
            .Setup(c => c.GetWalletAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

        var result = await _resolver.ResolveAsync("did:sorcha:w:unknown");

        result.IsResolved.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ResolveAsync_WalletServiceUnavailable_ReturnsFailure()
    {
        _walletClientMock
            .Setup(c => c.GetWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var result = await _resolver.ResolveAsync("did:sorcha:w:someaddr");

        result.IsResolved.Should().BeFalse();
        result.Error.Should().Contain("Failed to resolve");
    }

    // --- Register DID Resolution ---

    [Fact]
    public async Task ResolveAsync_ValidRegisterDid_ReturnsPublicKey()
    {
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var controlPayload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = new RegisterControlRecord
            {
                RegisterId = registerId,
                Name = "Test",
                CreatedAt = DateTimeOffset.UtcNow,
                Attestations =
                [
                    new RegisterAttestation
                    {
                        Role = RegisterRole.Owner,
                        Subject = "did:sorcha:w:owner1",
                        PublicKey = "ownerPubKey==",
                        Signature = "sig==",
                        Algorithm = SignatureAlgorithm.ED25519,
                        GrantedAt = DateTimeOffset.UtcNow
                    }
                ]
            }
        };

        var payloadJson = JsonSerializer.Serialize(controlPayload);
        var base64Payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson));

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(registerId, txId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionModel
            {
                TxId = txId,
                Payloads = [new PayloadModel { Data = base64Payload }]
            });

        var result = await _resolver.ResolveAsync($"did:sorcha:r:{registerId}:t:{txId}");

        result.IsResolved.Should().BeTrue();
        result.PublicKey.Should().Be("ownerPubKey==");
        result.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task ResolveAsync_UnknownRegisterTransaction_ReturnsFailure()
    {
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(registerId, txId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        var result = await _resolver.ResolveAsync($"did:sorcha:r:{registerId}:t:{txId}");

        result.IsResolved.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    // --- Invalid DID ---

    [Fact]
    public async Task ResolveAsync_InvalidDid_ReturnsFailure()
    {
        var result = await _resolver.ResolveAsync("not-a-did");

        result.IsResolved.Should().BeFalse();
        result.Error.Should().Contain("Invalid DID format");
    }

    [Fact]
    public async Task ResolveAsync_EmptyDid_ReturnsFailure()
    {
        var result = await _resolver.ResolveAsync("");

        result.IsResolved.Should().BeFalse();
    }
}
