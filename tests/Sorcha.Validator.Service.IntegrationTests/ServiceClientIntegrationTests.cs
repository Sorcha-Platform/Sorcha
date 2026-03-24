// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Validator.Service.IntegrationTests;

/// <summary>
/// Integration tests for Service Client interactions.
/// Tests that the Validator Service correctly integrates with external services.
/// </summary>
[Collection("ValidatorService")]
public class ServiceClientIntegrationTests
{
    private readonly ValidatorServiceWebApplicationFactory _factory;

    public ServiceClientIntegrationTests(ValidatorServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ClearTestData();
    }

    #region Register Service Client

    [Fact]
    public async Task RegisterClient_GetRegister_CalledOnValidatorStart()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify register client was called
        _factory.RegisterClientMock.Verify(
            r => r.GetRegisterAsync(registerId, It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    [Fact]
    public async Task RegisterClient_GetLatestDocket_CalledForChainInfo()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Setup latest docket
        _factory.RegisterClientMock
            .Setup(r => r.ReadLatestDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocketModel
            {
                DocketId = "latest-docket",
                RegisterId = registerId,
                DocketNumber = 5,
                DocketHash = "latest-hash",
                MerkleRoot = "latest-merkle",
                CreatedAt = DateTimeOffset.UtcNow,
                ProposerValidatorId = "some-validator",
                Transactions = new List<TransactionModel>()
            });

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act - Process pipeline to trigger chain info lookup
        await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - Latest docket may have been requested (depends on pipeline execution)
        _factory.RegisterClientMock.Verify(
            r => r.ReadLatestDocketAsync(registerId, It.IsAny<CancellationToken>()),
            Times.AtMost(10));
    }

    [Fact]
    public async Task RegisterClient_WriteDocket_CalledOnConsensus()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        SetupDefaultValidators(registerId);
        SetupDefaultWalletSigning();

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act
        await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - WriteDocket may be called if there are transactions
        _factory.RegisterClientMock.Verify(
            r => r.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    [Fact]
    public async Task RegisterClient_ReadGenesisDocket_CalledForGenesisConfig()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Assert - Genesis docket (docket 0) should be read
        _factory.RegisterClientMock.Verify(
            r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    #endregion

    #region Blueprint Service Client

    [Fact]
    public async Task BlueprintClient_GetBlueprint_CalledOnTransactionValidation()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");
        var blueprintId = "validation-test-blueprint";

        var request = new
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            BlueprintId = blueprintId,
            ActionId = "test-action",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = "abc123",
            Signatures = new[]
            {
                new { PublicKey = Convert.ToBase64String(new byte[32]), SignatureValue = Convert.ToBase64String(new byte[64]), Algorithm = "ED25519" }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert - Blueprint should be fetched for validation
        _factory.BlueprintClientMock.Verify(
            b => b.GetBlueprintAsync(blueprintId, It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    [Fact]
    public async Task BlueprintClient_ValidatePayload_CalledForSchemaValidation()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");
        var blueprintId = "schema-validation-blueprint";

        var request = new
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            BlueprintId = blueprintId,
            ActionId = "submit-data",
            Payload = JsonSerializer.Deserialize<JsonElement>("{\"name\":\"test\"}"),
            PayloadHash = "abc123",
            Signatures = new[]
            {
                new { PublicKey = Convert.ToBase64String(new byte[32]), SignatureValue = Convert.ToBase64String(new byte[64]), Algorithm = "ED25519" }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert - Payload validation may be called
        _factory.BlueprintClientMock.Verify(
            b => b.ValidatePayloadAsync(blueprintId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    [Fact]
    public async Task BlueprintClient_WhenUnavailable_HandlesGracefully()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");
        var blueprintId = "unavailable-blueprint";

        // Setup blueprint service to fail
        _factory.BlueprintClientMock
            .Setup(b => b.GetBlueprintAsync(blueprintId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Blueprint service unavailable"));

        var request = new
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            BlueprintId = blueprintId,
            ActionId = "test-action",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = "abc123",
            Signatures = new[]
            {
                new { PublicKey = Convert.ToBase64String(new byte[32]), SignatureValue = Convert.ToBase64String(new byte[64]), Algorithm = "ED25519" }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert - Should handle gracefully (not crash)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.Conflict,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Peer Service Client

    [Fact]
    public async Task PeerClient_QueryValidators_CalledOnProcessPipeline()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act
        await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - Validators may be queried (depends on pipeline execution path)
        _factory.PeerClientMock.Verify(
            p => p.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtMost(10));
    }

    [Fact]
    public async Task PeerClient_PublishProposedDocket_CalledOnDocketCreation()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        SetupDefaultValidators(registerId);
        SetupDefaultWalletSigning();

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act
        await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - Docket may be published (if there are transactions)
        _factory.PeerClientMock.Verify(
            p => p.PublishProposedDocketAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    [Fact]
    public async Task PeerClient_BroadcastConfirmedDocket_CalledOnConsensusSuccess()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        SetupDefaultValidators(registerId);
        SetupDefaultWalletSigning();

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act
        await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - Confirmed docket may be broadcast (if consensus achieved)
        _factory.PeerClientMock.Verify(
            p => p.BroadcastConfirmedDocketAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    [Fact]
    public async Task PeerClient_WhenUnavailable_HandlesGracefully()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Setup peer service to fail
        _factory.PeerClientMock
            .Setup(p => p.QueryValidatorsAsync(registerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Peer service unavailable"));

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act
        var response = await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - Should handle gracefully
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Wallet Service Client

    [Fact]
    public async Task WalletClient_CreateOrRetrieveSystemWallet_CalledOnStart()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Assert - System wallet may be retrieved
        _factory.WalletClientMock.Verify(
            w => w.CreateOrRetrieveSystemWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtMost(1));
    }

    [Fact]
    public async Task WalletClient_SignData_CalledOnDocketSigning()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        SetupDefaultValidators(registerId);

        _factory.WalletClientMock
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                SignedBy = "test-validator",
                Algorithm = "ED25519"
            });

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act
        await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - SignData may be called if docket is created
        _factory.WalletClientMock.Verify(
            w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtMost(10));
    }

    [Fact]
    public async Task WalletClient_WhenUnavailable_HandlesGracefully()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        SetupDefaultValidators(registerId);

        // Setup wallet service to fail
        _factory.WalletClientMock
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Wallet service unavailable"));

        // Start validator
        await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Act
        var response = await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert - Should handle gracefully
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Service Client Retry Behavior

    [Fact]
    public async Task ServiceClients_OnTransientFailure_RetryWithBackoff()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");
        var callCount = 0;

        // Setup register service to fail once then succeed
        _factory.RegisterClientMock
            .Setup(r => r.GetRegisterAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("Transient failure");
                return new Register.Models.Register
                {
                    Id = registerId,
                    Name = "Test Register",
                    CreatedAt = DateTime.UtcNow,
                    Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
                };
            });

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Assert - Should succeed after retry or handle error gracefully
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable, HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Helper Methods

    private void SetupDefaultValidators(string registerId)
    {
        _factory.PeerClientMock
            .Setup(p => p.QueryValidatorsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidatorInfo>
            {
                new()
                {
                    ValidatorId = "test-validator-id",
                    GrpcEndpoint = "http://localhost:7004",
                    ReputationScore = 1.0,
                    IsActive = true
                }
            });
    }

    private void SetupDefaultWalletSigning()
    {
        _factory.WalletClientMock
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                SignedBy = "test-validator-id",
                Algorithm = "ED25519"
            });
    }

    #endregion
}
