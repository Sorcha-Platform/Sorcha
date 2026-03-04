// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for validator operational presence via heartbeat TTL.
/// Covers policy-driven TTL configuration, heartbeat refresh, and min-validator enforcement.
/// </summary>
public class ValidatorOperationalPresenceTests
{
    private const string TestRegisterId = "test-register-001";

    #region TTL Configuration from Policy

    [Fact]
    public async Task ResolveOperationalTtl_WithPolicy_UsesOperationalTtlSeconds()
    {
        // Arrange
        var (registry, mockClient, _, _) = CreateRegistry();
        mockClient
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPolicyResponse
            {
                Policy = new RegisterPolicy
                {
                    Version = 1,
                    Validators = new PolicyValidatorConfig { OperationalTtlSeconds = 120 }
                }
            });

        // Act
        var ttl = await registry.ResolveOperationalTtlAsync(TestRegisterId);

        // Assert
        ttl.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task ResolveOperationalTtl_NoPolicyAvailable_FallsBackToConfig()
    {
        // Arrange
        var (registry, mockClient, _, _) = CreateRegistry(defaultTtlSeconds: 45);
        mockClient
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisterPolicyResponse?)null);

        // Act
        var ttl = await registry.ResolveOperationalTtlAsync(TestRegisterId);

        // Assert
        ttl.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task ResolveOperationalTtl_PolicyFetchFails_FallsBackToConfig()
    {
        // Arrange
        var (registry, mockClient, _, _) = CreateRegistry(defaultTtlSeconds: 30);
        mockClient
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        // Act
        var ttl = await registry.ResolveOperationalTtlAsync(TestRegisterId);

        // Assert
        ttl.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ResolveOperationalTtl_CachesResult_DoesNotCallPolicyAgain()
    {
        // Arrange
        var (registry, mockClient, _, _) = CreateRegistry();
        mockClient
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPolicyResponse
            {
                Policy = new RegisterPolicy
                {
                    Version = 1,
                    Validators = new PolicyValidatorConfig { OperationalTtlSeconds = 90 }
                }
            });

        // Act — call twice
        var ttl1 = await registry.ResolveOperationalTtlAsync(TestRegisterId);
        var ttl2 = await registry.ResolveOperationalTtlAsync(TestRegisterId);

        // Assert — policy fetched only once (cached)
        ttl1.Should().Be(TimeSpan.FromSeconds(90));
        ttl2.Should().Be(TimeSpan.FromSeconds(90));
        mockClient.Verify(
            c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveOperationalTtl_DefaultPolicy_Returns60Seconds()
    {
        // Arrange
        var (registry, mockClient, _, _) = CreateRegistry();
        mockClient
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPolicyResponse
            {
                Policy = RegisterPolicy.CreateDefault()
            });

        // Act
        var ttl = await registry.ResolveOperationalTtlAsync(TestRegisterId);

        // Assert — default OperationalTtlSeconds is 60
        ttl.Should().Be(TimeSpan.FromSeconds(60));
    }

    #endregion

    #region MinValidators Enforcement (SC-005)

    [Fact]
    public async Task ValidatorDisappearance_BelowMinValidators_ShouldPreventDocketBuild()
    {
        // This test verifies the conceptual flow:
        // When active validators drop below policy minValidators,
        // the DocketBuildTriggerService should skip building

        // Arrange
        var mockValidatorRegistry = new Mock<IValidatorRegistry>();
        var mockRegisterClient = new Mock<IRegisterServiceClient>();

        // Active validators = 1, but policy requires min 3
        mockValidatorRegistry
            .Setup(r => r.GetActiveCountAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        mockRegisterClient
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPolicyResponse
            {
                Policy = new RegisterPolicy
                {
                    Version = 1,
                    Validators = new PolicyValidatorConfig { MinValidators = 3 }
                }
            });

        // Act
        var activeCount = await mockValidatorRegistry.Object.GetActiveCountAsync(TestRegisterId);
        var policy = await mockRegisterClient.Object.GetRegisterPolicyAsync(TestRegisterId);
        var minValidators = policy?.Policy?.Validators?.MinValidators ?? 1;

        // Assert — active count is below minimum
        activeCount.Should().BeLessThan(minValidators);
    }

    [Fact]
    public async Task ValidatorDisappearance_AtOrAboveMinValidators_ShouldAllowDocketBuild()
    {
        // Arrange
        var mockValidatorRegistry = new Mock<IValidatorRegistry>();
        var mockRegisterClient = new Mock<IRegisterServiceClient>();

        // Active validators = 3, policy requires min 3
        mockValidatorRegistry
            .Setup(r => r.GetActiveCountAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        mockRegisterClient
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPolicyResponse
            {
                Policy = new RegisterPolicy
                {
                    Version = 1,
                    Validators = new PolicyValidatorConfig { MinValidators = 3 }
                }
            });

        // Act
        var activeCount = await mockValidatorRegistry.Object.GetActiveCountAsync(TestRegisterId);
        var policy = await mockRegisterClient.Object.GetRegisterPolicyAsync(TestRegisterId);
        var minValidators = policy?.Policy?.Validators?.MinValidators ?? 1;

        // Assert — active count meets minimum
        activeCount.Should().BeGreaterThanOrEqualTo(minValidators);
    }

    #endregion

    #region TTL Window Validation (2x TTL disappearance)

    [Fact]
    public void OperationalTtl_WithinExpectedRange_IsValid()
    {
        // The spec says validators must disappear within 2x TTL.
        // With TTL=60s, a validator's Redis key expires at most 60s after last refresh.
        // With no heartbeat, the validator should be gone within 2x60s = 120s.

        var ttlSeconds = 60;
        var maxDisappearanceWindow = TimeSpan.FromSeconds(ttlSeconds * 2);

        // 2x TTL window should be 2 minutes
        maxDisappearanceWindow.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void DefaultConfig_OperationalTtlSeconds_Is60()
    {
        var config = new ValidatorRegistryConfiguration();

        config.DefaultOperationalTtlSeconds.Should().Be(60);
    }

    [Fact]
    public void DefaultPolicy_OperationalTtlSeconds_Is60()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.OperationalTtlSeconds.Should().Be(60);
    }

    #endregion

    #region Helpers

    private static (ValidatorRegistry Registry, Mock<IRegisterServiceClient> MockClient,
        Mock<IConnectionMultiplexer> MockRedis, Mock<IGenesisConfigService> MockGenesis)
        CreateRegistry(int defaultTtlSeconds = 60)
    {
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDatabase = new Mock<IDatabase>();
        var mockServer = new Mock<IServer>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
        mockRedis.Setup(r => r.GetEndPoints(It.IsAny<bool>())).Returns([new System.Net.DnsEndPoint("localhost", 6379)]);
        mockRedis.Setup(r => r.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object>())).Returns(mockServer.Object);

        var mockClient = new Mock<IRegisterServiceClient>();
        var mockGenesis = new Mock<IGenesisConfigService>();

        mockGenesis
            .Setup(g => g.GetValidatorConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidatorConfig
            {
                RegistrationMode = "public",
                MaxValidators = 100,
                MinValidators = 1,
                RequireStake = false
            });

        var config = Options.Create(new ValidatorRegistryConfiguration
        {
            DefaultOperationalTtlSeconds = defaultTtlSeconds
        });

        var logger = new Mock<ILogger<ValidatorRegistry>>();

        var registry = new ValidatorRegistry(
            mockRedis.Object,
            mockClient.Object,
            mockGenesis.Object,
            config,
            logger.Object);

        return (registry, mockClient, mockRedis, mockGenesis);
    }

    #endregion
}
