// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for RegisterCreationOrchestrator
/// </summary>
public class RegisterCreationOrchestratorTests
{
    private readonly Mock<ILogger<RegisterCreationOrchestrator>> _mockLogger;
    private readonly Mock<RegisterManager> _mockRegisterManager;
    private readonly Mock<TransactionManager> _mockTransactionManager;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<ICryptoModule> _mockCryptoModule;
    private readonly Mock<IValidatorServiceClient> _mockValidatorClient;
    private readonly Mock<ISystemWalletSigningService> _mockSigningService;
    private readonly Mock<IPendingRegistrationStore> _mockPendingStore;
    private readonly Mock<IPeerServiceClient> _mockPeerClient;
    private readonly RegisterCreationOrchestrator _orchestrator;

    public RegisterCreationOrchestratorTests()
    {
        _mockLogger = new Mock<ILogger<RegisterCreationOrchestrator>>();
        _mockRegisterManager = new Mock<RegisterManager>(
            Mock.Of<Sorcha.Register.Core.Storage.IRegisterRepository>(),
            Mock.Of<Sorcha.Register.Core.Events.IEventPublisher>());
        _mockTransactionManager = new Mock<TransactionManager>(
            Mock.Of<Sorcha.Register.Core.Storage.IRegisterRepository>(),
            Mock.Of<Sorcha.Register.Core.Events.IEventPublisher>());
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockCryptoModule = new Mock<ICryptoModule>();
        _mockValidatorClient = new Mock<IValidatorServiceClient>();
        _mockSigningService = new Mock<ISystemWalletSigningService>();

        // Default signing service setup
        _mockSigningService
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[] { 1, 2, 3, 4 },
                PublicKey = new byte[] { 5, 6, 7, 8 },
                Algorithm = "ED25519",
                WalletAddress = "system-wallet-001"
            });

        // Default generic submission setup
        _mockValidatorClient
            .Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "tx-placeholder",
                RegisterId = "reg-placeholder",
                AddedAt = DateTimeOffset.UtcNow
            });

        _mockPendingStore = new Mock<IPendingRegistrationStore>();
        _mockPeerClient = new Mock<IPeerServiceClient>();

        // Default: UpdateRegisterStatusAsync returns the register with new status
        _mockRegisterManager
            .Setup(m => m.UpdateRegisterStatusAsync(
                It.IsAny<string>(),
                It.IsAny<Sorcha.Register.Models.Enums.RegisterStatus>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, Sorcha.Register.Models.Enums.RegisterStatus status, CancellationToken _) =>
                new Sorcha.Register.Models.Register { Id = id, Status = status });

        // Default pending store behavior: store and retrieve
        var store = new Dictionary<string, PendingRegistration>();
        _mockPendingStore
            .Setup(s => s.Add(It.IsAny<string>(), It.IsAny<PendingRegistration>()))
            .Callback<string, PendingRegistration>((key, value) => store[key] = value);
        _mockPendingStore
            .Setup(s => s.TryRemove(It.IsAny<string>(), out It.Ref<PendingRegistration?>.IsAny))
            .Returns((string key, out PendingRegistration? value) =>
            {
                if (store.TryGetValue(key, out var v))
                {
                    value = v;
                    store.Remove(key);
                    return true;
                }
                value = null;
                return false;
            });

        _orchestrator = new RegisterCreationOrchestrator(
            _mockLogger.Object,
            _mockRegisterManager.Object,
            _mockTransactionManager.Object,
            _mockWalletClient.Object,
            _mockHashProvider.Object,
            _mockCryptoModule.Object,
            _mockValidatorClient.Object,
            _mockSigningService.Object,
            _mockPendingStore.Object,
            _mockPeerClient.Object);
    }

    #region InitiateAsync Tests

    [Fact]
    public async Task InitiateAsync_WithValidRequest_ShouldReturnInitiateResponse()
    {
        // Arrange
        var request = new InitiateRegisterCreationRequest
        {
            Name = "Test Register",
            Description = "Test Description",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            }
        };

        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]); // SHA-256 hash

        // Act
        var response = await _orchestrator.InitiateAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.RegisterId.Should().NotBeNullOrEmpty();
        response.RegisterId.Should().HaveLength(32); // GUID without hyphens
        response.AttestationsToSign.Should().HaveCount(1);
        response.AttestationsToSign[0].Role.Should().Be(RegisterRole.Owner);
        response.AttestationsToSign[0].AttestationData.Subject.Should().Be("did:sorcha:w:wallet-001");
        response.AttestationsToSign[0].DataToSign.Should().NotBeNullOrEmpty();
        response.Nonce.Should().NotBeNullOrEmpty();
        response.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task InitiateAsync_WithAdditionalAdmins_ShouldIncludeAllAttestations()
    {
        // Arrange
        var request = new InitiateRegisterCreationRequest
        {
            Name = "Multi-Admin Register",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            },
            AdditionalAdmins = new List<AdditionalAdminInfo>
            {
                new()
                {
                    UserId = "user-002",
                    WalletId = "wallet-002",
                    Role = RegisterRole.Admin
                },
                new()
                {
                    UserId = "user-003",
                    WalletId = "wallet-003",
                    Role = RegisterRole.Auditor
                }
            }
        };

        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        // Act
        var response = await _orchestrator.InitiateAsync(request);

        // Assert
        response.AttestationsToSign.Should().HaveCount(3);
        response.AttestationsToSign[0].Role.Should().Be(RegisterRole.Owner);
        response.AttestationsToSign[0].AttestationData.Subject.Should().Be("did:sorcha:w:wallet-001");
        response.AttestationsToSign[1].Role.Should().Be(RegisterRole.Admin);
        response.AttestationsToSign[1].AttestationData.Subject.Should().Be("did:sorcha:w:wallet-002");
        response.AttestationsToSign[2].Role.Should().Be(RegisterRole.Auditor);
        response.AttestationsToSign[2].AttestationData.Subject.Should().Be("did:sorcha:w:wallet-003");
    }

    [Fact]
    public async Task InitiateAsync_ShouldComputeCanonicalJsonHash()
    {
        // Arrange
        var request = new InitiateRegisterCreationRequest
        {
            Name = "Test Register",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            }
        };

        byte[]? capturedBytes = null;
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Callback<byte[], HashType>((data, _) => capturedBytes = data)
            .Returns(new byte[32]);

        // Act
        await _orchestrator.InitiateAsync(request);

        // Assert
        capturedBytes.Should().NotBeNull();
        var json = Encoding.UTF8.GetString(capturedBytes!);
        json.Should().Contain("\"registerId\"");
        json.Should().Contain("\"registerName\"");
        json.Should().Contain("Test Register");
        _mockHashProvider.Verify(
            h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256),
            Times.Once);
    }

    [Fact]
    public async Task InitiateAsync_WithAdvertiseTrue_ShouldStoreInPending()
    {
        // Arrange
        var request = new InitiateRegisterCreationRequest
        {
            Name = "Public Register",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            },
            Advertise = true
        };

        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        // Act
        await _orchestrator.InitiateAsync(request);

        // Assert
        _mockPendingStore.Verify(
            s => s.Add(It.IsAny<string>(), It.Is<PendingRegistration>(p => p.Advertise)),
            Times.Once);
    }

    #endregion

    #region FinalizeAsync Tests

    [Fact]
    public async Task FinalizeAsync_WithValidSignatures_ShouldCreateRegister()
    {
        // Arrange
        var initiateRequest = new InitiateRegisterCreationRequest
        {
            Name = "Test Register",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            }
        };

        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        var initiateResponse = await _orchestrator.InitiateAsync(initiateRequest);

        var signedAttestations = initiateResponse.AttestationsToSign.Select(a => new SignedAttestation
        {
            AttestationData = a.AttestationData,
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519
        }).ToList();

        var finalizeRequest = new FinalizeRegisterCreationRequest
        {
            RegisterId = initiateResponse.RegisterId,
            Nonce = initiateResponse.Nonce,
            SignedAttestations = signedAttestations
        };

        var createdRegister = new Sorcha.Register.Models.Register
        {
            Id = initiateResponse.RegisterId,
            Name = "Test Register",
            TenantId = "tenant-123",
            CreatedAt = DateTime.UtcNow
        };

        _mockCryptoModule
            .Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);

        _mockRegisterManager
            .Setup(m => m.CreateRegisterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdRegister);

        // Act
        var response = await _orchestrator.FinalizeAsync(finalizeRequest);

        // Assert
        response.Should().NotBeNull();
        response.RegisterId.Should().Be(initiateResponse.RegisterId);
        response.Status.Should().Be("created");
        response.GenesisTransactionId.Should().NotBeNullOrEmpty();
        response.GenesisDocketId.Should().Be("0");

        // Verify signing service was called
        _mockSigningService.Verify(
            s => s.SignAsync(
                initiateResponse.RegisterId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                "sorcha:register-control",
                "Genesis",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify generic submission endpoint was used (not genesis)
        _mockValidatorClient.Verify(
            v => v.SubmitTransactionAsync(
                It.Is<TransactionSubmission>(s =>
                    s.RegisterId == initiateResponse.RegisterId &&
                    s.Metadata != null &&
                    s.Metadata["Type"] == "Genesis" &&
                    s.Metadata["RegisterName"] == "Test Register"),
                It.IsAny<CancellationToken>()),
            Times.Once);

    }

    [Fact]
    public async Task FinalizeAsync_WithInvalidNonce_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var initiateRequest = new InitiateRegisterCreationRequest
        {
            Name = "Test Register",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            }
        };

        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        var initiateResponse = await _orchestrator.InitiateAsync(initiateRequest);

        var finalizeRequest = new FinalizeRegisterCreationRequest
        {
            RegisterId = initiateResponse.RegisterId,
            Nonce = "invalid-nonce", // Wrong nonce
            SignedAttestations = new List<SignedAttestation>()
        };

        // Act & Assert
        var act = async () => await _orchestrator.FinalizeAsync(finalizeRequest);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*nonce*");
    }

    [Fact]
    public async Task FinalizeAsync_WithExpiredPending_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var finalizeRequest = new FinalizeRegisterCreationRequest
        {
            RegisterId = "non-existent-register",
            Nonce = "some-nonce",
            SignedAttestations = new List<SignedAttestation>()
        };

        // Act & Assert
        var act = async () => await _orchestrator.FinalizeAsync(finalizeRequest);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Pending registration not found*");
    }

    [Fact]
    public async Task FinalizeAsync_WithInvalidSignature_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var initiateRequest = new InitiateRegisterCreationRequest
        {
            Name = "Test Register",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            }
        };

        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        var initiateResponse = await _orchestrator.InitiateAsync(initiateRequest);

        var signedAttestations = initiateResponse.AttestationsToSign.Select(a => new SignedAttestation
        {
            AttestationData = a.AttestationData,
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519
        }).ToList();

        var finalizeRequest = new FinalizeRegisterCreationRequest
        {
            RegisterId = initiateResponse.RegisterId,
            Nonce = initiateResponse.Nonce,
            SignedAttestations = signedAttestations
        };

        _mockCryptoModule
            .Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.InvalidSignature); // Invalid signature

        // Act & Assert
        var act = async () => await _orchestrator.FinalizeAsync(finalizeRequest);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Invalid signature*");
    }

    #endregion

    #region Signature Algorithm Mapping Tests

    [Theory]
    [InlineData(SignatureAlgorithm.ED25519, WalletNetworks.ED25519)]
    [InlineData(SignatureAlgorithm.NISTP256, WalletNetworks.NISTP256)]
    [InlineData(SignatureAlgorithm.RSA4096, WalletNetworks.RSA4096)]
    public async Task FinalizeAsync_ShouldMapSignatureAlgorithmsCorrectly(
        SignatureAlgorithm inputAlgorithm,
        WalletNetworks expectedWalletNetwork)
    {
        // Arrange
        var initiateRequest = new InitiateRegisterCreationRequest
        {
            Name = "Test Register",
            TenantId = "tenant-123",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-001", WalletId = "wallet-001" }
            }
        };

        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        var initiateResponse = await _orchestrator.InitiateAsync(initiateRequest);

        var signedAttestations = initiateResponse.AttestationsToSign.Select(a => new SignedAttestation
        {
            AttestationData = a.AttestationData,
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = inputAlgorithm
        }).ToList();

        var finalizeRequest = new FinalizeRegisterCreationRequest
        {
            RegisterId = initiateResponse.RegisterId,
            Nonce = initiateResponse.Nonce,
            SignedAttestations = signedAttestations
        };

        byte? capturedNetwork = null;
        _mockCryptoModule
            .Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<byte[], byte[], byte, byte[], CancellationToken>((_, _, network, _, _) =>
                capturedNetwork = network)
            .ReturnsAsync(CryptoStatus.Success);

        _mockRegisterManager
            .Setup(m => m.CreateRegisterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = initiateResponse.RegisterId,
                Name = "Test Register",
                TenantId = "tenant-123",
                CreatedAt = DateTime.UtcNow
            });

        // Act
        await _orchestrator.FinalizeAsync(finalizeRequest);

        // Assert
        capturedNetwork.Should().Be((byte)expectedWalletNetwork);
    }

    #endregion
}
