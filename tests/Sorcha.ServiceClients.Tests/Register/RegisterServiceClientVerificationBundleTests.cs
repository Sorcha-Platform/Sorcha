// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.Register.Models;
using Sorcha.Serialization;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Register;

namespace Sorcha.ServiceClients.Tests.Register;

/// <summary>
/// #1680: <see cref="RegisterServiceClient.GetVerificationBundleAsync"/> must surface the Register
/// Service's real HTTP status and reason for a refusal instead of collapsing every non-success
/// response (404 no-such-transaction, 409 not-sealed-yet) into a bare null.
/// </summary>
public class RegisterServiceClientVerificationBundleTests
{
    private readonly Mock<IServiceAuthClient> _serviceAuthMock;
    private readonly Mock<ILogger<RegisterServiceClient>> _loggerMock;
    private readonly IConfiguration _configuration;

    public RegisterServiceClientVerificationBundleTests()
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

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, string? jsonBody = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        var response = new HttpResponseMessage(statusCode);
        if (jsonBody != null)
        {
            response.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return handlerMock;
    }

    [Fact]
    public async Task GetVerificationBundleAsync_409_ReturnsRefusalWithServerReason()
    {
        // Arrange — the real Register Service body shape for "not sealed yet".
        var body = JsonSerializer.Serialize(new
        {
            error = "Transaction has not been sealed yet. A verification bundle requires a sealed receipt.",
            txId = "tx-1"
        });
        var handler = CreateMockHandler(HttpStatusCode.Conflict, body);
        var client = CreateClient(handler);

        // Act
        var outcome = await client.GetVerificationBundleAsync("reg-1", "tx-1");

        // Assert
        outcome.Should().NotBeNull();
        outcome!.Bundle.Should().BeNull();
        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.StatusCode.Should().Be(409);
        outcome.Refusal.Reason.Should().Be("Transaction has not been sealed yet. A verification bundle requires a sealed receipt.");
    }

    [Fact]
    public async Task GetVerificationBundleAsync_404_ReturnsRefusalWithServerReason()
    {
        var body = JsonSerializer.Serialize(new
        {
            error = "Transaction 'tx-1' not found in register 'reg-1'"
        });
        var handler = CreateMockHandler(HttpStatusCode.NotFound, body);
        var client = CreateClient(handler);

        var outcome = await client.GetVerificationBundleAsync("reg-1", "tx-1");

        outcome.Should().NotBeNull();
        outcome!.Bundle.Should().BeNull();
        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.StatusCode.Should().Be(404);
        outcome.Refusal.Reason.Should().Be("Transaction 'tx-1' not found in register 'reg-1'");
    }

    [Fact]
    public async Task GetVerificationBundleAsync_Success_ReturnsBundle()
    {
        var bundle = new VerificationBundle
        {
            TransactionId = "tx-1",
            RegisterId = "reg-1",
            Credential = JsonSerializer.Deserialize<JsonElement>("[]"),
            Receipt = new TransactionReceipt
            {
                ReceiptId = "rcpt-1",
                TransactionId = "tx-1",
                RegisterId = "reg-1",
                DocketNumber = 3,
                MerkleRoot = "root",
                InclusionProof = new MerkleInclusionProof
                {
                    TransactionHash = "abc",
                    DocketNumber = 3,
                    MerkleRoot = "root",
                    ProofPath = [],
                    LeafIndex = 0,
                    TreeSize = 1
                },
                Signatures = [],
                SealedAt = DateTimeOffset.UtcNow
            },
            RevocationStatus = new TransactionStatusResponse
            {
                TransactionId = "tx-1",
                Status = TransactionLifecycleStatus.Active
            },
            ExportedAt = DateTimeOffset.UtcNow,
            ValidatorPublicKeys = []
        };
        var body = JsonSerializer.Serialize(bundle, SorchaJson.Options);
        var handler = CreateMockHandler(HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var outcome = await client.GetVerificationBundleAsync("reg-1", "tx-1");

        outcome.Should().NotBeNull();
        outcome!.Refusal.Should().BeNull();
        outcome.Bundle.Should().NotBeNull();
        outcome.Bundle!.TransactionId.Should().Be("tx-1");
    }
}
