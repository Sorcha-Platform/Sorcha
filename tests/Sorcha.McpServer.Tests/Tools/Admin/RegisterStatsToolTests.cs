// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4: RegisterStatsTool reads via the typed <see cref="IRegisterServiceClient"/>
/// (routes pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class RegisterStatsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private RegisterStatsTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<RegisterStatsTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_stats")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    [Fact]
    public async Task GetRegisterStatsAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_stats")).Returns(false);

        var result = await CreateTool().GetRegisterStatsAsync();

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
        result.OverallStats.Should().BeNull();
    }

    [Fact]
    public async Task GetRegisterStatsAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_stats")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(false);

        var result = await CreateTool().GetRegisterStatsAsync();

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task GetRegisterStatsAsync_OverallStats_ReturnsHealthyResult()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetStatsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStatsResponse { RegisterCount = 5, TransactionCount = 200 });

        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RegisterSummaryInfo>
            {
                new() { Id = "reg-1", Name = "Test Register 1", Status = "Active", TenantId = "tenant-1", Height = 100, CreatedAt = DateTimeOffset.UtcNow.AddDays(-10) },
                new() { Id = "reg-2", Name = "Test Register 2", Status = "Active", TenantId = "tenant-1", Height = 50, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) }
            });

        var result = await CreateTool().GetRegisterStatsAsync();

        result.Status.Should().Be("Healthy");
        result.Message.Should().Contain("5 registers");
        result.OverallStats.Should().NotBeNull();
        result.OverallStats!.RegisterCount.Should().Be(5);
        result.OverallStats.RecentRegisters.Should().HaveCount(2);
        result.RegisterStats.Should().BeNull(); // No registerId provided

        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetRegisterStatsAsync_WithRegisterId_ReturnsTransactionStats()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetStatsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStatsResponse { RegisterCount = 1 });
        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _registerClientMock
            .Setup(c => c.GetRegisterTransactionStatsAsync("reg-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterTransactionStatistics
            {
                TotalTransactions = 150,
                UniqueWallets = 25,
                UniqueSenders = 10,
                UniqueRecipients = 20,
                TotalPayloads = 300,
                EarliestTransaction = DateTime.UtcNow.AddDays(-30),
                LatestTransaction = DateTime.UtcNow.AddHours(-1)
            });

        var result = await CreateTool().GetRegisterStatsAsync("reg-123");

        result.Status.Should().Be("Healthy");
        result.Message.Should().Contain("reg-123");
        result.RegisterStats.Should().NotBeNull();
        result.RegisterStats!.RegisterId.Should().Be("reg-123");
        result.RegisterStats.TotalTransactions.Should().Be(150);
        result.RegisterStats.UniqueWallets.Should().Be(25);
        result.RegisterStats.TotalPayloads.Should().Be(300);
    }

    [Fact]
    public async Task GetRegisterStatsAsync_RegisterNotFound_ReturnsPartialResult()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetStatsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStatsResponse { RegisterCount = 5 });
        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _registerClientMock
            .Setup(c => c.GetRegisterTransactionStatsAsync("nonexistent-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisterTransactionStatistics?)null);

        var result = await CreateTool().GetRegisterStatsAsync("nonexistent-register");

        result.Status.Should().Be("Partial");
        result.Message.Should().Contain("could not retrieve stats");
        result.OverallStats.Should().NotBeNull();
        result.RegisterStats.Should().BeNull();
    }

    [Fact]
    public async Task GetRegisterStatsAsync_OverallStatsThrows_ReturnsUnknownResult()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetStatsAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("backend boom"));

        var result = await CreateTool().GetRegisterStatsAsync();

        result.Status.Should().Be("Unknown");
        result.Message.Should().Contain("Unable to retrieve");
    }

    [Fact]
    public async Task GetRegisterStatsAsync_ResponseTimeIsRecorded()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetStatsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStatsResponse { RegisterCount = 0 });
        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateTool().GetRegisterStatsAsync();

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
