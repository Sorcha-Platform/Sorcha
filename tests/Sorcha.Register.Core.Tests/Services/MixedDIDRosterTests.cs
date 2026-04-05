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

public class MixedDIDRosterTests
{
    private readonly Mock<IWalletServiceClient> _walletClientMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly DIDResolver _resolver;

    public MixedDIDRosterTests()
    {
        _walletClientMock = new Mock<IWalletServiceClient>();
        _registerClientMock = new Mock<IRegisterServiceClient>();
        var logger = new Mock<ILogger<DIDResolver>>();
        _resolver = new DIDResolver(_walletClientMock.Object, _registerClientMock.Object, logger.Object);
    }

    [Fact]
    public async Task ResolveAsync_WalletDid_ReturnsWalletPublicKey()
    {
        var walletAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
        _walletClientMock
            .Setup(c => c.GetWalletAsync(walletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = walletAddress,
                Name = "Wallet A",
                PublicKey = "walletPubKeyA==",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "user1",
                Tenant = "tenant1"
            });

        var result = await _resolver.ResolveAsync($"did:sorcha:w:{walletAddress}");

        result.IsResolved.Should().BeTrue();
        result.PublicKey.Should().Be("walletPubKeyA==");
        result.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task ResolveAsync_RegisterDid_ReturnsRegisterPublicKey()
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
                        Subject = $"did:sorcha:r:{registerId}:t:{txId}",
                        PublicKey = "registerPubKey==",
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
        result.PublicKey.Should().Be("registerPubKey==");
    }

    [Fact]
    public async Task ResolveAsync_MixedDIDs_BothResolve()
    {
        var walletAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        _walletClientMock
            .Setup(c => c.GetWalletAsync(walletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = walletAddress,
                Name = "Wallet A",
                PublicKey = "walletPubKeyA==",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "user1",
                Tenant = "tenant1"
            });

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
                        Subject = $"did:sorcha:r:{registerId}:t:{txId}",
                        PublicKey = "registerPubKey==",
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

        // Resolve both DID types
        var walletResult = await _resolver.ResolveAsync($"did:sorcha:w:{walletAddress}");
        var registerResult = await _resolver.ResolveAsync($"did:sorcha:r:{registerId}:t:{txId}");

        walletResult.IsResolved.Should().BeTrue();
        walletResult.PublicKey.Should().Be("walletPubKeyA==");

        registerResult.IsResolved.Should().BeTrue();
        registerResult.PublicKey.Should().Be("registerPubKey==");
    }

    [Fact]
    public async Task ResolveAsync_WalletNotFound_RegisterStillResolves()
    {
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        _walletClientMock
            .Setup(c => c.GetWalletAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

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
                        Subject = "did:sorcha:w:unknown",
                        PublicKey = "key==",
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

        var walletResult = await _resolver.ResolveAsync("did:sorcha:w:unknown");
        var registerResult = await _resolver.ResolveAsync($"did:sorcha:r:{registerId}:t:{txId}");

        walletResult.IsResolved.Should().BeFalse();
        registerResult.IsResolved.Should().BeTrue();
    }
}
