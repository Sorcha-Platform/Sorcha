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
/// Tests for register creation with optional RegisterPolicy (Feature 048, US1)
/// </summary>
public class RegisterCreationPolicyTests
{
    private readonly Mock<ILogger<RegisterCreationOrchestrator>> _mockLogger;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<IPendingRegistrationStore> _mockPendingStore;
    private readonly RegisterCreationOrchestrator _orchestrator;
    private PendingRegistration? _capturedPending;

    public RegisterCreationPolicyTests()
    {
        _mockLogger = new Mock<ILogger<RegisterCreationOrchestrator>>();
        var mockRegisterManager = new Mock<RegisterManager>(
            Mock.Of<Sorcha.Register.Core.Storage.IRegisterRepository>(),
            Mock.Of<Sorcha.Register.Core.Events.IEventPublisher>());
        var mockTransactionManager = new Mock<TransactionManager>(
            Mock.Of<Sorcha.Register.Core.Storage.IRegisterRepository>(),
            Mock.Of<Sorcha.Register.Core.Events.IEventPublisher>());
        var mockWalletClient = new Mock<IWalletServiceClient>();
        _mockHashProvider = new Mock<IHashProvider>();
        var mockCryptoModule = new Mock<ICryptoModule>();
        var mockValidatorClient = new Mock<IValidatorServiceClient>();
        var mockSigningService = new Mock<ISystemWalletSigningService>();
        _mockPendingStore = new Mock<IPendingRegistrationStore>();
        var mockPeerClient = new Mock<IPeerServiceClient>();

        // Default hash provider returns deterministic bytes
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType _) =>
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                return sha.ComputeHash(data);
            });

        // Capture stored pending registration
        _mockPendingStore
            .Setup(s => s.Add(It.IsAny<string>(), It.IsAny<PendingRegistration>()))
            .Callback<string, PendingRegistration>((_, pending) => _capturedPending = pending);

        _orchestrator = new RegisterCreationOrchestrator(
            _mockLogger.Object,
            mockRegisterManager.Object,
            mockTransactionManager.Object,
            mockWalletClient.Object,
            _mockHashProvider.Object,
            mockCryptoModule.Object,
            mockValidatorClient.Object,
            mockSigningService.Object,
            _mockPendingStore.Object,
            mockPeerClient.Object);
    }

    [Fact]
    public async Task InitiateAsync_WithCustomPolicy_StoresPolicyOnControlRecord()
    {
        // Arrange
        var customPolicy = new RegisterPolicy
        {
            Version = 1,
            Governance = new PolicyGovernanceConfig
            {
                QuorumFormula = QuorumFormula.Supermajority,
                ProposalTtlDays = 14
            },
            Validators = new PolicyValidatorConfig
            {
                RegistrationMode = RegistrationMode.Consent,
                MinValidators = 3,
                MaxValidators = 50
            },
            Consensus = new PolicyConsensusConfig
            {
                SignatureThresholdMin = 3,
                SignatureThresholdMax = 7
            },
            LeaderElection = new PolicyLeaderElectionConfig
            {
                Mechanism = ElectionMechanism.Rotating
            }
        };

        var request = new InitiateRegisterCreationRequest
        {
            Name = "PolicyTest",
            TenantId = "tenant-1",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-1", WalletId = "wallet-1" }
            },
            Policy = customPolicy
        };

        // Act
        var response = await _orchestrator.InitiateAsync(request);

        // Assert
        response.Should().NotBeNull();
        _capturedPending.Should().NotBeNull();
        _capturedPending!.ControlRecord.RegisterPolicy.Should().NotBeNull();
        _capturedPending.ControlRecord.RegisterPolicy!.Governance.QuorumFormula
            .Should().Be(QuorumFormula.Supermajority);
        _capturedPending.ControlRecord.RegisterPolicy.Validators.RegistrationMode
            .Should().Be(RegistrationMode.Consent);
        _capturedPending.ControlRecord.RegisterPolicy.Validators.MinValidators
            .Should().Be(3);
        _capturedPending.ControlRecord.RegisterPolicy.Consensus.SignatureThresholdMin
            .Should().Be(3);
    }

    [Fact]
    public async Task InitiateAsync_WithoutPolicy_AppliesDefaultPolicy()
    {
        // Arrange
        var request = new InitiateRegisterCreationRequest
        {
            Name = "NoPolicyTest",
            TenantId = "tenant-1",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-1", WalletId = "wallet-1" }
            }
            // Policy is null (omitted)
        };

        // Act
        var response = await _orchestrator.InitiateAsync(request);

        // Assert
        response.Should().NotBeNull();
        _capturedPending.Should().NotBeNull();
        _capturedPending!.ControlRecord.RegisterPolicy.Should().NotBeNull();

        var defaultPolicy = RegisterPolicy.CreateDefault();
        _capturedPending.ControlRecord.RegisterPolicy!.Version.Should().Be(defaultPolicy.Version);
        _capturedPending.ControlRecord.RegisterPolicy.Governance.QuorumFormula
            .Should().Be(defaultPolicy.Governance.QuorumFormula);
        _capturedPending.ControlRecord.RegisterPolicy.Validators.RegistrationMode
            .Should().Be(defaultPolicy.Validators.RegistrationMode);
        _capturedPending.ControlRecord.RegisterPolicy.Validators.MinValidators
            .Should().Be(defaultPolicy.Validators.MinValidators);
    }

    [Fact]
    public async Task InitiateAsync_DefaultPolicy_HasExpectedValues()
    {
        // Arrange
        var request = new InitiateRegisterCreationRequest
        {
            Name = "DefaultsTest",
            TenantId = "tenant-1",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-1", WalletId = "wallet-1" }
            }
        };

        // Act
        await _orchestrator.InitiateAsync(request);

        // Assert
        var policy = _capturedPending!.ControlRecord.RegisterPolicy!;
        policy.Version.Should().Be(1);
        policy.Governance.QuorumFormula.Should().Be(QuorumFormula.StrictMajority);
        policy.Governance.ProposalTtlDays.Should().Be(7);
        policy.Governance.OwnerCanBypassQuorum.Should().BeTrue();
        policy.Validators.RegistrationMode.Should().Be(RegistrationMode.Public);
        policy.Validators.MinValidators.Should().Be(1);
        policy.Validators.MaxValidators.Should().Be(100);
        policy.Validators.RequireStake.Should().BeFalse();
        policy.Validators.OperationalTtlSeconds.Should().Be(60);
        policy.Consensus.SignatureThresholdMin.Should().Be(2);
        policy.Consensus.SignatureThresholdMax.Should().Be(10);
        policy.Consensus.MaxTransactionsPerDocket.Should().Be(1000);
        policy.LeaderElection.Mechanism.Should().Be(ElectionMechanism.Rotating);
        policy.LeaderElection.HeartbeatIntervalMs.Should().Be(1000);
        policy.LeaderElection.LeaderTimeoutMs.Should().Be(5000);
    }

    [Fact]
    public async Task InitiateAsync_CustomPolicyWithConsentMode_PreservesApprovedValidators()
    {
        // Arrange
        var policy = RegisterPolicy.CreateDefault();
        policy.Validators.RegistrationMode = RegistrationMode.Consent;
        policy.Validators.ApprovedValidators = new List<ApprovedValidator>
        {
            new()
            {
                Did = "did:sorcha:validator-1",
                PublicKey = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                ApprovedAt = DateTimeOffset.UtcNow,
                ApprovedBy = "did:sorcha:admin-1"
            }
        };

        var request = new InitiateRegisterCreationRequest
        {
            Name = "ConsentModeTest",
            TenantId = "tenant-1",
            Owners = new List<OwnerInfo>
            {
                new() { UserId = "user-1", WalletId = "wallet-1" }
            },
            Policy = policy
        };

        // Act
        await _orchestrator.InitiateAsync(request);

        // Assert
        _capturedPending!.ControlRecord.RegisterPolicy!.Validators.RegistrationMode
            .Should().Be(RegistrationMode.Consent);
        _capturedPending.ControlRecord.RegisterPolicy.Validators.ApprovedValidators
            .Should().HaveCount(1);
        _capturedPending.ControlRecord.RegisterPolicy.Validators.ApprovedValidators[0].Did
            .Should().Be("did:sorcha:validator-1");
    }
}
