// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Dashboard;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for dashboard auto-refresh timer behavior.
/// Verifies that DashboardService supports repeated polling with correct data.
/// </summary>
public class DashboardServiceRefreshTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly DashboardService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public DashboardServiceRefreshTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        var logger = new Mock<ILogger<DashboardService>>();
        _service = new DashboardService(_httpClient, logger.Object);
    }

    public void Dispose()
    {
        foreach (var r in _responses) r.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SetupSequentialResponses(params DashboardStatsViewModel[] stats)
    {
        var queue = new Queue<DashboardStatsViewModel>(stats);
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var stat = queue.Count > 0 ? queue.Dequeue() : stats.Last();
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(stat)
                };
                _responses.Add(response);
                return response;
            });
    }

    private void SetupErrorResponse()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                _responses.Add(response);
                return response;
            });
    }

    [Fact]
    public async Task GetDashboardStatsAsync_SuccessiveCalls_ReturnsUpdatedData()
    {
        var stats1 = new DashboardStatsViewModel { ActiveBlueprints = 5, IsLoaded = true };
        var stats2 = new DashboardStatsViewModel { ActiveBlueprints = 8, IsLoaded = true };
        SetupSequentialResponses(stats1, stats2);

        var result1 = await _service.GetDashboardStatsAsync();
        var result2 = await _service.GetDashboardStatsAsync();

        result1.ActiveBlueprints.Should().Be(5);
        result2.ActiveBlueprints.Should().Be(8);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_SuccessAfterFailure_RecoversGracefully()
    {
        // First call fails, second succeeds
        var callCount = 0;
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                HttpResponseMessage response;
                if (callCount == 1)
                {
                    response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
                else
                {
                    response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new DashboardStatsViewModel
                        {
                            ActiveBlueprints = 10,
                            IsLoaded = true
                        })
                    };
                }
                _responses.Add(response);
                return response;
            });

        var result1 = await _service.GetDashboardStatsAsync();
        var result2 = await _service.GetDashboardStatsAsync();

        result1.IsLoaded.Should().BeFalse();
        result2.IsLoaded.Should().BeTrue();
        result2.ActiveBlueprints.Should().Be(10);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_MultipleFailures_AlwaysReturnsNotLoaded()
    {
        SetupErrorResponse();

        var result1 = await _service.GetDashboardStatsAsync();
        var result2 = await _service.GetDashboardStatsAsync();
        var result3 = await _service.GetDashboardStatsAsync();

        result1.IsLoaded.Should().BeFalse();
        result2.IsLoaded.Should().BeFalse();
        result3.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task GetDashboardStatsAsync_Success_ReturnsAllFields()
    {
        var stats = new DashboardStatsViewModel
        {
            ActiveBlueprints = 12,
            TotalWallets = 23,
            RecentTransactions = 45,
            ConnectedPeers = 4,
            ActiveRegisters = 5,
            TotalOrganizations = 3,
            IsLoaded = true
        };
        SetupSequentialResponses(stats);

        var result = await _service.GetDashboardStatsAsync();

        result.IsLoaded.Should().BeTrue();
        result.ActiveBlueprints.Should().Be(12);
        result.TotalWallets.Should().Be(23);
        result.RecentTransactions.Should().Be(45);
        result.ConnectedPeers.Should().Be(4);
        result.ActiveRegisters.Should().Be(5);
        result.TotalOrganizations.Should().Be(3);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_NetworkError_ReturnsNotLoaded()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _service.GetDashboardStatsAsync();

        result.IsLoaded.Should().BeFalse();
    }
}
