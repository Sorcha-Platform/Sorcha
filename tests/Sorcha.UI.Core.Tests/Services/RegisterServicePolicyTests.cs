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
/// Unit tests for RegisterService policy methods: GetPolicyAsync,
/// GetPolicyHistoryAsync, and ProposePolicyUpdateAsync.
/// </summary>
public class RegisterServicePolicyTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<RegisterService>> _loggerMock;
    private readonly RegisterService _registerService;

    private const string TestRegisterId = "reg-001";

    public RegisterServicePolicyTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _loggerMock = new Mock<ILogger<RegisterService>>();
        _registerService = new RegisterService(_httpClient, _loggerMock.Object);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region GetPolicyAsync

    [Fact]
    public async Task GetPolicyAsync_Success_ReturnsExpectedPolicy()
    {
        // Arrange
        var expectedPolicy = new RegisterPolicyViewModel
        {
            RegisterId = TestRegisterId,
            Version = 3,
            MinValidators = 2,
            MaxValidators = 5,
            SignatureThreshold = 2,
            RegistrationMode = "Restricted",
            TransitionMode = "Immediate",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "admin@test.com"
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expectedPolicy)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains($"/api/registers/{TestRegisterId}/policy")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _registerService.GetPolicyAsync(TestRegisterId);

        // Assert
        result.Should().NotBeNull();
        result!.RegisterId.Should().Be(TestRegisterId);
        result.Version.Should().Be(3);
        result.MinValidators.Should().Be(2);
        result.MaxValidators.Should().Be(5);
        result.SignatureThreshold.Should().Be(2);
        result.RegistrationMode.Should().Be("Restricted");
        result.UpdatedBy.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task GetPolicyAsync_ServerError_ReturnsNull()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains($"/api/registers/{TestRegisterId}/policy")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _registerService.GetPolicyAsync(TestRegisterId);

        // Assert
        result.Should().BeNull("API returned error status code");
    }

    [Fact]
    public async Task GetPolicyAsync_NetworkException_ReturnsNull()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _registerService.GetPolicyAsync(TestRegisterId);

        // Assert
        result.Should().BeNull("exception occurred during API call");
    }

    [Fact]
    public async Task GetPolicyAsync_CorrectUrl_SendsGetToExpectedPath()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new RegisterPolicyViewModel { RegisterId = TestRegisterId })
        };

        Uri? capturedUri = null;
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
            .ReturnsAsync(response);

        // Act
        await _registerService.GetPolicyAsync(TestRegisterId);

        // Assert
        capturedUri.Should().NotBeNull();
        capturedUri!.PathAndQuery.Should().Be($"/api/registers/{TestRegisterId}/policy");
    }

    #endregion

    #region GetPolicyHistoryAsync

    [Fact]
    public async Task GetPolicyHistoryAsync_Success_ReturnsExpectedHistory()
    {
        // Arrange
        var expectedHistory = new PolicyHistoryViewModel
        {
            RegisterId = TestRegisterId,
            TotalCount = 3,
            Versions =
            [
                new PolicyVersionViewModel { Version = 3, UpdatedBy = "admin@test.com", UpdatedAt = DateTimeOffset.UtcNow },
                new PolicyVersionViewModel { Version = 2, UpdatedBy = "admin@test.com", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
                new PolicyVersionViewModel { Version = 1, UpdatedBy = "system", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-7) }
            ]
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expectedHistory)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains($"/api/registers/{TestRegisterId}/policy/history")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _registerService.GetPolicyHistoryAsync(TestRegisterId, 1, 10);

        // Assert
        result.Should().NotBeNull();
        result.RegisterId.Should().Be(TestRegisterId);
        result.TotalCount.Should().Be(3);
        result.Versions.Should().HaveCount(3);
        result.Versions[0].Version.Should().Be(3);
        result.Versions[2].UpdatedBy.Should().Be("system");
    }

    [Fact]
    public async Task GetPolicyHistoryAsync_ServerError_ReturnsDefaultWithRegisterId()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains($"/api/registers/{TestRegisterId}/policy/history")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _registerService.GetPolicyHistoryAsync(TestRegisterId, 1, 10);

        // Assert
        result.RegisterId.Should().Be(TestRegisterId, "failure should return default with RegisterId set");
        result.Versions.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPolicyHistoryAsync_NetworkException_ReturnsDefaultWithRegisterId()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _registerService.GetPolicyHistoryAsync(TestRegisterId, 1, 10);

        // Assert
        result.RegisterId.Should().Be(TestRegisterId, "exception should return default with RegisterId set");
        result.Versions.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPolicyHistoryAsync_CorrectUrl_IncludesPageAndPageSizeQueryParams()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PolicyHistoryViewModel { RegisterId = TestRegisterId })
        };

        Uri? capturedUri = null;
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
            .ReturnsAsync(response);

        // Act
        await _registerService.GetPolicyHistoryAsync(TestRegisterId, 2, 25);

        // Assert
        capturedUri.Should().NotBeNull();
        capturedUri!.PathAndQuery.Should().Be($"/api/registers/{TestRegisterId}/policy/history?page=2&pageSize=25");
    }

    #endregion

    #region ProposePolicyUpdateAsync

    [Fact]
    public async Task ProposePolicyUpdateAsync_Success_ReturnsProposal()
    {
        // Arrange
        var policyFields = new RegisterPolicyFields
        {
            MinValidators = 3,
            MaxValidators = 7,
            SignatureThreshold = 3,
            RegistrationMode = "Restricted",
            TransitionMode = "Voting"
        };

        var expectedProposal = new PolicyUpdateProposalViewModel
        {
            ProposalId = "prop-001",
            RegisterId = TestRegisterId,
            ProposedVersion = 4,
            Status = "Proposed",
            RequiredVotes = 3,
            CurrentVotes = 0
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expectedProposal)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains($"/api/registers/{TestRegisterId}/policy/update")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _registerService.ProposePolicyUpdateAsync(TestRegisterId, policyFields);

        // Assert
        result.Should().NotBeNull();
        result!.ProposalId.Should().Be("prop-001");
        result.RegisterId.Should().Be(TestRegisterId);
        result.ProposedVersion.Should().Be(4);
        result.Status.Should().Be("Proposed");
        result.RequiredVotes.Should().Be(3);
        result.CurrentVotes.Should().Be(0);
    }

    [Fact]
    public async Task ProposePolicyUpdateAsync_ServerError_ReturnsNull()
    {
        // Arrange
        var policyFields = new RegisterPolicyFields
        {
            MinValidators = 2,
            MaxValidators = 5,
            SignatureThreshold = 2
        };

        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains($"/api/registers/{TestRegisterId}/policy/update")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _registerService.ProposePolicyUpdateAsync(TestRegisterId, policyFields);

        // Assert
        result.Should().BeNull("API returned error status code");
    }

    [Fact]
    public async Task ProposePolicyUpdateAsync_NetworkException_ReturnsNull()
    {
        // Arrange
        var policyFields = new RegisterPolicyFields
        {
            MinValidators = 1,
            MaxValidators = 10,
            SignatureThreshold = 1
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _registerService.ProposePolicyUpdateAsync(TestRegisterId, policyFields);

        // Assert
        result.Should().BeNull("exception occurred during API call");
    }

    [Fact]
    public async Task ProposePolicyUpdateAsync_CorrectUrl_SendsPostToExpectedPath()
    {
        // Arrange
        var policyFields = new RegisterPolicyFields
        {
            MinValidators = 2,
            MaxValidators = 8,
            SignatureThreshold = 2
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PolicyUpdateProposalViewModel { RegisterId = TestRegisterId })
        };

        Uri? capturedUri = null;
        HttpMethod? capturedMethod = null;
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUri = req.RequestUri;
                capturedMethod = req.Method;
            })
            .ReturnsAsync(response);

        // Act
        await _registerService.ProposePolicyUpdateAsync(TestRegisterId, policyFields);

        // Assert
        capturedUri.Should().NotBeNull();
        capturedMethod.Should().Be(HttpMethod.Post);
        capturedUri!.PathAndQuery.Should().Be($"/api/registers/{TestRegisterId}/policy/update");
    }

    #endregion
}
