// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for ValidatorAdminService new methods (consent queue, metrics, threshold, config).
/// </summary>
public class ValidatorAdminServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<ValidatorAdminService>> _loggerMock;
    private readonly ValidatorAdminService _service;

    public ValidatorAdminServiceTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _loggerMock = new Mock<ILogger<ValidatorAdminService>>();
        _service = new ValidatorAdminService(_httpClient, _loggerMock.Object);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    private void SetupResponse(HttpMethod method, string urlFragment, HttpStatusCode statusCode, object? content = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = content != null ? JsonContent.Create(content) : new StringContent(statusCode.ToString())
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == method &&
                    req.RequestUri!.ToString().Contains(urlFragment)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void SetupException(string urlFragment)
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains(urlFragment)),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
    }

    #endregion

    #region GetConsentQueueAsync

    [Fact]
    public async Task GetConsentQueueAsync_Success_ReturnsConsentQueue()
    {
        // Arrange
        var expected = new ConsentQueueViewModel
        {
            TotalPending = 3,
            Registers = [new RegisterConsentGroup { RegisterId = "reg-1", RegisterName = "Test Register", PendingValidators = [new(), new(), new()] }]
        };
        SetupResponse(HttpMethod.Get, "/api/admin/validators/consent-queue", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetConsentQueueAsync();

        // Assert
        result.TotalPending.Should().Be(3);
        result.Registers.Should().HaveCount(1);
        result.Registers[0].RegisterId.Should().Be("reg-1");
    }

    [Fact]
    public async Task GetConsentQueueAsync_ApiFailure_ReturnsEmptyDefault()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/admin/validators/consent-queue", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetConsentQueueAsync();

        // Assert
        result.TotalPending.Should().Be(0);
        result.Registers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConsentQueueAsync_Exception_ReturnsEmptyDefault()
    {
        // Arrange
        SetupException("/api/admin/validators/consent-queue");

        // Act
        var result = await _service.GetConsentQueueAsync();

        // Assert
        result.TotalPending.Should().Be(0);
        result.Registers.Should().BeEmpty();
    }

    #endregion

    #region GetPendingValidatorsAsync

    [Fact]
    public async Task GetPendingValidatorsAsync_Success_ReturnsPendingList()
    {
        // Arrange
        var expected = new List<PendingValidatorViewModel>
        {
            new() { ValidatorId = "val-1", RegisterId = "reg-1", RegisterName = "Test", RequestedAt = DateTimeOffset.UtcNow, Endpoint = "https://val1:5000" }
        };
        SetupResponse(HttpMethod.Get, "/api/validators/reg-1/pending", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetPendingValidatorsAsync("reg-1");

        // Assert
        result.Should().HaveCount(1);
        result[0].ValidatorId.Should().Be("val-1");
        result[0].Endpoint.Should().Be("https://val1:5000");
    }

    [Fact]
    public async Task GetPendingValidatorsAsync_ApiFailure_ReturnsEmptyList()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/validators/reg-1/pending", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetPendingValidatorsAsync("reg-1");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingValidatorsAsync_Exception_ReturnsEmptyList()
    {
        // Arrange
        SetupException("/api/validators/reg-1/pending");

        // Act
        var result = await _service.GetPendingValidatorsAsync("reg-1");

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region ApproveValidatorAsync

    [Fact]
    public async Task ApproveValidatorAsync_Success_ReturnsTrue()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/validators/reg-1/val-1/approve", HttpStatusCode.OK);

        // Act
        var result = await _service.ApproveValidatorAsync("reg-1", "val-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveValidatorAsync_ApiFailure_ReturnsFalse()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/validators/reg-1/val-1/approve", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.ApproveValidatorAsync("reg-1", "val-1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ApproveValidatorAsync_Exception_ReturnsFalse()
    {
        // Arrange
        SetupException("/api/validators/reg-1/val-1/approve");

        // Act
        var result = await _service.ApproveValidatorAsync("reg-1", "val-1");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region RejectValidatorAsync

    [Fact]
    public async Task RejectValidatorAsync_Success_ReturnsTrue()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/validators/reg-1/val-1/reject", HttpStatusCode.OK);

        // Act
        var result = await _service.RejectValidatorAsync("reg-1", "val-1", "Not trusted");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RejectValidatorAsync_NoReason_ReturnsTrue()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/validators/reg-1/val-1/reject", HttpStatusCode.OK);

        // Act
        var result = await _service.RejectValidatorAsync("reg-1", "val-1", null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RejectValidatorAsync_ApiFailure_ReturnsFalse()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/validators/reg-1/val-1/reject", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.RejectValidatorAsync("reg-1", "val-1", "reason");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RejectValidatorAsync_Exception_ReturnsFalse()
    {
        // Arrange
        SetupException("/api/validators/reg-1/val-1/reject");

        // Act
        var result = await _service.RejectValidatorAsync("reg-1", "val-1", "reason");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region RefreshApprovedValidatorsAsync

    [Fact]
    public async Task RefreshApprovedValidatorsAsync_Success_ReturnsList()
    {
        // Arrange
        var expected = new List<ApprovedValidatorInfo>
        {
            new() { ValidatorId = "val-1", Name = "Validator 1", ApprovedAt = DateTimeOffset.UtcNow }
        };
        SetupResponse(HttpMethod.Post, "/api/validators/reg-1/refresh", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.RefreshApprovedValidatorsAsync("reg-1");

        // Assert
        result.Should().HaveCount(1);
        result[0].ValidatorId.Should().Be("val-1");
    }

    [Fact]
    public async Task RefreshApprovedValidatorsAsync_ApiFailure_ReturnsEmptyList()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/validators/reg-1/refresh", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.RefreshApprovedValidatorsAsync("reg-1");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshApprovedValidatorsAsync_Exception_ReturnsEmptyList()
    {
        // Arrange
        SetupException("/api/validators/reg-1/refresh");

        // Act
        var result = await _service.RefreshApprovedValidatorsAsync("reg-1");

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetAggregatedMetricsAsync

    [Fact]
    public async Task GetAggregatedMetricsAsync_Success_ReturnsMetrics()
    {
        // Arrange
        var expected = new AggregatedMetricsViewModel
        {
            ValidationSuccessRate = 99.5,
            DocketsProposed = 150,
            QueueDepth = 5,
            CacheHitRatio = 0.85
        };
        SetupResponse(HttpMethod.Get, "/api/validator/metrics", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetAggregatedMetricsAsync();

        // Assert
        result.ValidationSuccessRate.Should().Be(99.5);
        result.DocketsProposed.Should().Be(150);
        result.QueueDepth.Should().Be(5);
        result.CacheHitRatio.Should().Be(0.85);
    }

    [Fact]
    public async Task GetAggregatedMetricsAsync_ApiFailure_ReturnsDefault()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/validator/metrics", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetAggregatedMetricsAsync();

        // Assert
        result.ValidationSuccessRate.Should().Be(0);
        result.DocketsProposed.Should().Be(0);
    }

    [Fact]
    public async Task GetAggregatedMetricsAsync_Exception_ReturnsDefault()
    {
        // Arrange
        SetupException("/api/validator/metrics");

        // Act
        var result = await _service.GetAggregatedMetricsAsync();

        // Assert
        result.ValidationSuccessRate.Should().Be(0);
        result.DocketsProposed.Should().Be(0);
    }

    #endregion

    #region GetValidationMetricsAsync

    [Fact]
    public async Task GetValidationMetricsAsync_Success_ReturnsMetrics()
    {
        // Arrange
        var expected = new ValidationSummaryViewModel();
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/validation", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetValidationMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetValidationMetricsAsync_ApiFailure_ReturnsDefault()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/validation", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetValidationMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetValidationMetricsAsync_Exception_ReturnsDefault()
    {
        // Arrange
        SetupException("/api/validator/metrics/validation");

        // Act
        var result = await _service.GetValidationMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region GetConsensusMetricsAsync

    [Fact]
    public async Task GetConsensusMetricsAsync_Success_ReturnsMetrics()
    {
        // Arrange
        var expected = new ConsensusSummaryViewModel();
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/consensus", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetConsensusMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConsensusMetricsAsync_ApiFailure_ReturnsDefault()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/consensus", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetConsensusMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConsensusMetricsAsync_Exception_ReturnsDefault()
    {
        // Arrange
        SetupException("/api/validator/metrics/consensus");

        // Act
        var result = await _service.GetConsensusMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region GetPoolMetricsAsync

    [Fact]
    public async Task GetPoolMetricsAsync_Success_ReturnsMetrics()
    {
        // Arrange
        var expected = new PoolSummaryViewModel();
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/pools", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetPoolMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPoolMetricsAsync_ApiFailure_ReturnsDefault()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/pools", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetPoolMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPoolMetricsAsync_Exception_ReturnsDefault()
    {
        // Arrange
        SetupException("/api/validator/metrics/pools");

        // Act
        var result = await _service.GetPoolMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region GetCacheMetricsAsync

    [Fact]
    public async Task GetCacheMetricsAsync_Success_ReturnsMetrics()
    {
        // Arrange
        var expected = new CacheSummaryViewModel();
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/caches", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetCacheMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCacheMetricsAsync_ApiFailure_ReturnsDefault()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/caches", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetCacheMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCacheMetricsAsync_Exception_ReturnsDefault()
    {
        // Arrange
        SetupException("/api/validator/metrics/caches");

        // Act
        var result = await _service.GetCacheMetricsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region GetThresholdStatusAsync

    [Fact]
    public async Task GetThresholdStatusAsync_Success_ReturnsList()
    {
        // Arrange
        var expected = new List<ThresholdConfigViewModel>
        {
            new()
            {
                RegisterId = "reg-1",
                GroupPublicKey = "pub-key-1",
                Threshold = 2,
                TotalValidators = 3,
                ValidatorIds = ["val-1", "val-2", "val-3"],
                IsConfigured = true
            }
        };
        SetupResponse(HttpMethod.Get, "/api/v1/validators/threshold/status", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetThresholdStatusAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].RegisterId.Should().Be("reg-1");
        result[0].Threshold.Should().Be(2);
        result[0].TotalValidators.Should().Be(3);
        result[0].IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task GetThresholdStatusAsync_ApiFailure_ReturnsEmptyList()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/v1/validators/threshold/status", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetThresholdStatusAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetThresholdStatusAsync_Exception_ReturnsEmptyList()
    {
        // Arrange
        SetupException("/api/v1/validators/threshold/status");

        // Act
        var result = await _service.GetThresholdStatusAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region SetupThresholdAsync

    [Fact]
    public async Task SetupThresholdAsync_Success_ReturnsConfig()
    {
        // Arrange
        var request = new ThresholdSetupRequest
        {
            RegisterId = "reg-1",
            Threshold = 2,
            TotalValidators = 3,
            ValidatorIds = ["val-1", "val-2", "val-3"]
        };
        var expected = new ThresholdConfigViewModel
        {
            RegisterId = "reg-1",
            GroupPublicKey = "generated-pub-key",
            Threshold = 2,
            TotalValidators = 3,
            ValidatorIds = ["val-1", "val-2", "val-3"],
            IsConfigured = true
        };
        SetupResponse(HttpMethod.Post, "/api/v1/validators/threshold/setup", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.SetupThresholdAsync(request);

        // Assert
        result.RegisterId.Should().Be("reg-1");
        result.GroupPublicKey.Should().Be("generated-pub-key");
        result.Threshold.Should().Be(2);
        result.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task SetupThresholdAsync_ApiFailure_ThrowsHttpRequestException()
    {
        // Arrange
        var request = new ThresholdSetupRequest
        {
            RegisterId = "reg-1",
            Threshold = 2,
            TotalValidators = 3,
            ValidatorIds = ["val-1", "val-2", "val-3"]
        };
        SetupResponse(HttpMethod.Post, "/api/v1/validators/threshold/setup", HttpStatusCode.InternalServerError);

        // Act
        var act = () => _service.SetupThresholdAsync(request);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region GetConfigAsync

    [Fact]
    public async Task GetConfigAsync_Success_ReturnsConfig()
    {
        // Arrange
        var expected = new ValidatorConfigViewModel
        {
            Fields = new Dictionary<string, string>
            {
                ["ConsensusTimeout"] = "30",
                ["MaxPoolSize"] = "1000"
            },
            RedactedKeys = ["SecretKey", "ConnectionString"]
        };
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/config", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetConfigAsync();

        // Assert
        result.Fields.Should().HaveCount(2);
        result.Fields["ConsensusTimeout"].Should().Be("30");
        result.RedactedKeys.Should().Contain("SecretKey");
    }

    [Fact]
    public async Task GetConfigAsync_ApiFailure_ReturnsEmptyDefault()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/validator/metrics/config", HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetConfigAsync();

        // Assert
        result.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConfigAsync_Exception_ReturnsEmptyDefault()
    {
        // Arrange
        SetupException("/api/validator/metrics/config");

        // Act
        var result = await _service.GetConfigAsync();

        // Assert
        result.Fields.Should().BeEmpty();
    }

    #endregion
}
