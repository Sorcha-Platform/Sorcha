// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;

namespace Sorcha.ServiceClients.Tests.Register;

public class RegisterServiceClientGovernanceTests
{
    private readonly Mock<IServiceAuthClient> _serviceAuthMock;
    private readonly Mock<ILogger<RegisterServiceClient>> _loggerMock;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RegisterServiceClientGovernanceTests()
    {
        _serviceAuthMock = new Mock<IServiceAuthClient>();
        _serviceAuthMock.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        _loggerMock = new Mock<ILogger<RegisterServiceClient>>();

        var configData = new Dictionary<string, string?>
        {
            ["ServiceClients:RegisterService:Address"] = "http://localhost:5290"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private RegisterServiceClient CreateClient(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object);
        return new RegisterServiceClient(httpClient, _serviceAuthMock.Object, _configuration, _loggerMock.Object);
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, object? responseBody = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        var response = new HttpResponseMessage(statusCode);
        if (responseBody != null)
        {
            response.Content = new StringContent(
                JsonSerializer.Serialize(responseBody, JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return handlerMock;
    }

    #region ProposeGovernanceOperationAsync Tests

    [Fact]
    public async Task ProposeGovernanceOperationAsync_Success_ReturnsResponse()
    {
        // Arrange
        var responseBody = new GovernanceProposalResponse
        {
            TxId = "abc123",
            RegisterId = "reg-1",
            OperationType = "add",
            ProposerDid = "did:sorcha:w:owner",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = "Admin",
            Submitted = true
        };
        var handler = CreateMockHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);

        var request = new GovernanceProposalRequest
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin
        };

        // Act
        var result = await client.ProposeGovernanceOperationAsync("reg-1", request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("abc123", result.TxId);
        Assert.Equal("reg-1", result.RegisterId);
        Assert.Equal("add", result.OperationType);
        Assert.True(result.Submitted);
    }

    [Fact]
    public async Task ProposeGovernanceOperationAsync_BadRequest_ReturnsNull()
    {
        // Arrange
        var handler = CreateMockHandler(HttpStatusCode.BadRequest, new { error = "Validation failed" });
        var client = CreateClient(handler);

        var request = new GovernanceProposalRequest
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:nobody",
            TargetDid = "did:sorcha:w:target"
        };

        // Act
        var result = await client.ProposeGovernanceOperationAsync("reg-1", request);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ProposeGovernanceOperationAsync_ServerError_ReturnsNull()
    {
        // Arrange
        var handler = CreateMockHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var request = new GovernanceProposalRequest
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:owner",
            TargetDid = "did:sorcha:w:admin"
        };

        // Act
        var result = await client.ProposeGovernanceOperationAsync("reg-1", request);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ProposeGovernanceOperationAsync_SendsCorrectUrl()
    {
        // Arrange
        var handler = CreateMockHandler(HttpStatusCode.OK, new GovernanceProposalResponse { TxId = "x" });
        var client = CreateClient(handler);

        var request = new GovernanceProposalRequest
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner",
            TargetDid = "did:sorcha:w:newowner"
        };

        // Act
        await client.ProposeGovernanceOperationAsync("my-register", request);

        // Assert
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.PathAndQuery.Contains("/api/registers/my-register/governance/propose")),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region GetGovernanceProposalsAsync Tests

    [Fact]
    public async Task GetGovernanceProposalsAsync_Success_ReturnsProposalPage()
    {
        // Arrange
        var responseBody = new GovernanceProposalPage
        {
            Page = 1,
            PageSize = 20,
            Total = 2,
            Proposals =
            [
                // Feature 189 T046: the endpoint reports the SIGNED PAYLOAD (proposalId / proposedBy)
                // with a status derived from sealed content, not the tracking metadata it used to
                // read. This test builds its own body, so it proves the client can read this shape —
                // never that the server sends it. GovernanceProposalWireContractTests is what pins
                // the two together, by reflection over both types.
                new GovernanceProposalSummary
                {
                    ProposalId = "tx-1",
                    OperationType = "Add",
                    ProposedBy = "did:sorcha:w:owner",
                    TargetDid = "did:sorcha:w:admin1",
                    Status = "Open",
                    StatusReason = "None"
                },
                new GovernanceProposalSummary
                {
                    ProposalId = "tx-2",
                    OperationType = "Remove",
                    ProposedBy = "did:sorcha:w:owner",
                    TargetDid = "did:sorcha:w:admin1",
                    Status = "Enacted",
                    StatusReason = "QuorumMet"
                }
            ]
        };
        var handler = CreateMockHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);

        // Act
        var result = await client.GetGovernanceProposalsAsync("reg-1");

        // Assert
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Proposals.Count);
        Assert.Equal("tx-1", result.Proposals[0].ProposalId);
        Assert.Equal("Add", result.Proposals[0].OperationType);
        Assert.Equal("Open", result.Proposals[0].Status);
        Assert.Equal("Enacted", result.Proposals[1].Status);
    }

    [Fact]
    public async Task GetGovernanceProposalsAsync_NotFound_ReturnsEmptyPage()
    {
        // Arrange
        var handler = CreateMockHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        // Act
        var result = await client.GetGovernanceProposalsAsync("nonexistent");

        // Assert
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public async Task GetGovernanceProposalsAsync_SendsCorrectUrlWithPagination()
    {
        // Arrange
        var handler = CreateMockHandler(HttpStatusCode.OK, new GovernanceProposalPage());
        var client = CreateClient(handler);

        // Act
        await client.GetGovernanceProposalsAsync("reg-1", page: 2, pageSize: 10);

        // Assert
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Get &&
                r.RequestUri!.PathAndQuery.Contains("/api/registers/reg-1/governance/proposals") &&
                r.RequestUri.PathAndQuery.Contains("page=2") &&
                r.RequestUri.PathAndQuery.Contains("pageSize=10")),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion
}
