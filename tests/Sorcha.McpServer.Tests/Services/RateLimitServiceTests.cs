// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tests.Services;

public class RateLimitServiceTests : IDisposable
{
    private readonly Mock<ILogger<RateLimitService>> _loggerMock;
    private readonly RateLimitSettings _settings;
    private RateLimitService _service;

    public RateLimitServiceTests()
    {
        _loggerMock = new Mock<ILogger<RateLimitService>>();
        _settings = new RateLimitSettings
        {
            McpPerUserRequestsPerMinute = 10, // Low limit for testing
            McpPerTenantRequestsPerMinute = 20,
            McpAdminToolsRequestsPerMinute = 5
        };
        _service = CreateService();
    }

    private RateLimitService CreateService()
    {
        return new RateLimitService(
            Options.Create(_settings),
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public void CheckRateLimit_FirstRequest_IsAllowed()
    {
        // Act
        var result = _service.CheckRateLimit("user-1", "tenant-1", "designer");

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void CheckRateLimit_UnderUserLimit_IsAllowed()
    {
        // Act - make requests under the limit
        for (var i = 0; i < _settings.McpPerUserRequestsPerMinute - 1; i++)
        {
            var result = _service.CheckRateLimit("user-1", "tenant-1", "designer");
            result.IsAllowed.Should().BeTrue($"Request {i + 1} should be allowed");
        }
    }

    [Fact]
    public void CheckRateLimit_ExceedsUserLimit_IsRateLimited()
    {
        // Act - exhaust the limit
        for (var i = 0; i < _settings.McpPerUserRequestsPerMinute; i++)
        {
            _service.CheckRateLimit("user-1", "tenant-1", "designer");
        }

        // Next request should be rate limited
        var result = _service.CheckRateLimit("user-1", "tenant-1", "designer");

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.RetryAfter.Should().NotBeNull();
        result.Reason.Should().Contain("User rate limit");
    }

    [Fact]
    public void CheckRateLimit_DifferentUsers_IndependentLimits()
    {
        // Exhaust user-1's limit
        for (var i = 0; i < _settings.McpPerUserRequestsPerMinute; i++)
        {
            _service.CheckRateLimit("user-1", "tenant-1", "designer");
        }

        // user-2 should still be allowed
        var result = _service.CheckRateLimit("user-2", "tenant-1", "designer");

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CheckRateLimit_ExceedsTenantLimit_IsRateLimited()
    {
        // Use multiple users to exhaust tenant limit
        for (var i = 0; i < _settings.McpPerTenantRequestsPerMinute; i++)
        {
            var userId = $"user-{i % 5}"; // Cycle through 5 users
            _service.CheckRateLimit(userId, "tenant-1", "designer");
        }

        // Next request should be tenant rate limited
        var result = _service.CheckRateLimit("user-new", "tenant-1", "designer");

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Tenant rate limit");
    }

    [Fact]
    public void CheckRateLimit_AdminTool_HasSeparateLimit()
    {
        // Exhaust admin tool limit (lower than user limit)
        for (var i = 0; i < _settings.McpAdminToolsRequestsPerMinute; i++)
        {
            _service.CheckRateLimit("user-1", "tenant-1", "admin");
        }

        // Next admin tool request should be rate limited
        var adminResult = _service.CheckRateLimit("user-1", "tenant-1", "admin");
        adminResult.IsAllowed.Should().BeFalse();
        adminResult.Reason.Should().Contain("Admin tools rate limit");

        // But regular tool should still work (different category)
        var designerResult = _service.CheckRateLimit("user-1", "tenant-1", "designer");
        designerResult.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CheckRateLimit_DifferentTenants_IndependentLimits()
    {
        // Exhaust tenant-1's limit
        for (var i = 0; i < _settings.McpPerTenantRequestsPerMinute; i++)
        {
            _service.CheckRateLimit($"user-{i}", "tenant-1", "designer");
        }

        // tenant-2 should still be allowed
        var result = _service.CheckRateLimit("user-1", "tenant-2", "designer");

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void RateLimitResult_Allowed_HasCorrectProperties()
    {
        // Act
        var result = RateLimitResult.Allowed();

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.RetryAfter.Should().BeNull();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void RateLimitResult_Limited_HasCorrectProperties()
    {
        // Act
        var result = RateLimitResult.Limited(TimeSpan.FromSeconds(30), "Test reason");

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        result.Reason.Should().Be("Test reason");
    }
}
