// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Validator.Core.Validators;
using Sorcha.Validator.Service.Endpoints;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Peer;
using IBlueprintCache = Sorcha.Validator.Service.Services.Interfaces.IBlueprintCache;
using ITransactionPoolPoller = Sorcha.Validator.Service.Services.Interfaces.ITransactionPoolPoller;
using IValidationEngine = Sorcha.Validator.Service.Services.Interfaces.IValidationEngine;
using IVerifiedTransactionQueue = Sorcha.Validator.Service.Services.Interfaces.IVerifiedTransactionQueue;

namespace Sorcha.Validator.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for ValidationEndpoints
/// Tests cover API contract, validation logic, and error handling
/// </summary>
public class ValidationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ITransactionValidator> _mockTransactionValidator;
    private readonly Mock<IMemPoolManager> _mockMemPoolManager;
    private readonly Mock<ITransactionPoolPoller> _mockPoolPoller;
    private readonly Mock<IRegisterMonitoringRegistry> _mockMonitoringRegistry;
    private readonly Mock<IHashProvider> _mockHashProvider;

    public ValidationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _mockTransactionValidator = new Mock<ITransactionValidator>();
        _mockMemPoolManager = new Mock<IMemPoolManager>();
        _mockPoolPoller = new Mock<ITransactionPoolPoller>();
        _mockMonitoringRegistry = new Mock<IRegisterMonitoringRegistry>();
        _mockHashProvider = new Mock<IHashProvider>();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove all hosted services to prevent background service startup
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

                // Replace JWT authentication with test auth handler
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>("Test", _ => { });

                // Remove all validator service dependencies that need mocking
                RemoveService<ITransactionValidator>(services);
                RemoveService<IMemPoolManager>(services);
                RemoveService<IHashProvider>(services);
                RemoveService<IValidatorOrchestrator>(services);
                RemoveService<IConsensusEngine>(services);
                RemoveService<IDocketBuilder>(services);
                RemoveService<IGenesisManager>(services);
                RemoveService<IWalletServiceClient>(services);
                RemoveService<IRegisterServiceClient>(services);
                RemoveService<IPeerServiceClient>(services);

                // Add mocks for services used by ValidationEndpoints
                RemoveService<IRegisterMonitoringRegistry>(services);
                services.AddScoped<ITransactionValidator>(_ => _mockTransactionValidator.Object);
                services.AddSingleton<IMemPoolManager>(_ => _mockMemPoolManager.Object);
                services.AddSingleton<IRegisterMonitoringRegistry>(_ => _mockMonitoringRegistry.Object);
                services.AddScoped<IHashProvider>(_ => _mockHashProvider.Object);

                // Add mocks for dependencies of other services
                var mockOrchestrator = new Mock<IValidatorOrchestrator>();
                var mockConsensusEngine = new Mock<IConsensusEngine>();
                var mockDocketBuilder = new Mock<IDocketBuilder>();
                var mockGenesisManager = new Mock<IGenesisManager>();
                var mockWalletClient = new Mock<IWalletServiceClient>();
                var mockRegisterClient = new Mock<IRegisterServiceClient>();
                var mockPeerClient = new Mock<IPeerServiceClient>();

                services.AddSingleton<IValidatorOrchestrator>(_ => mockOrchestrator.Object);
                services.AddScoped<IConsensusEngine>(_ => mockConsensusEngine.Object);
                services.AddScoped<IDocketBuilder>(_ => mockDocketBuilder.Object);
                services.AddScoped<IGenesisManager>(_ => mockGenesisManager.Object);
                services.AddScoped<IWalletServiceClient>(_ => mockWalletClient.Object);
                services.AddScoped<IRegisterServiceClient>(_ => mockRegisterClient.Object);
                services.AddScoped<IPeerServiceClient>(_ => mockPeerClient.Object);

                // Add mocks for validation engine dependencies
                RemoveService<IBlueprintCache>(services);
                RemoveService<ITransactionPoolPoller>(services);
                RemoveService<IValidationEngine>(services);
                RemoveService<IVerifiedTransactionQueue>(services);
                services.AddSingleton<IBlueprintCache>(_ => new Mock<IBlueprintCache>().Object);
                services.AddSingleton<ITransactionPoolPoller>(_ => _mockPoolPoller.Object);
                services.AddScoped<IValidationEngine>(_ => new Mock<IValidationEngine>().Object);
                services.AddSingleton<IVerifiedTransactionQueue>(_ => new Mock<IVerifiedTransactionQueue>().Object);
            });
        });
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null) services.Remove(descriptor);
    }

    #region ValidateTransaction Tests

    [Fact]
    public async Task ValidateTransaction_WithValidTransaction_ReturnsOkAndAddsToMemPool()
    {
        // Arrange
        var client = _factory.CreateClient();

        var request = CreateValidTransactionRequest();

        // Setup mocks for successful validation
        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<JsonElement>(),
                It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(),
                It.IsAny<DateTimeOffset>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidatePayloadHash(It.IsAny<JsonElement>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidateSignatures(It.IsAny<List<TransactionSignature>>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockPoolPoller
            .Setup(p => p.SubmitTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isValid").GetBoolean().Should().BeTrue();
        result.GetProperty("added").GetBoolean().Should().BeTrue();
        result.GetProperty("transactionId").GetString().Should().Be(request.TransactionId);
    }

    [Fact]
    public async Task ValidateTransaction_WithInvalidStructure_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = CreateValidTransactionRequest();

        // Setup mock for failed structure validation
        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<JsonElement>(),
                It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(),
                It.IsAny<DateTimeOffset>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult
            {
                IsValid = false,
                Errors = new List<Sorcha.Validator.Core.Models.ValidationError>
                {
                    new Sorcha.Validator.Core.Models.ValidationError
                    {
                        Code = "TX_001",
                        Message = "Transaction ID is required",
                        Field = "transactionId"
                    }
                }
            });

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isValid").GetBoolean().Should().BeFalse();
        result.GetProperty("errors").EnumerateArray().Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateTransaction_WithInvalidPayloadHash_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = CreateValidTransactionRequest();

        // Structure validation passes
        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<JsonElement>(),
                It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(),
                It.IsAny<DateTimeOffset>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        // Payload hash validation fails
        _mockTransactionValidator
            .Setup(v => v.ValidatePayloadHash(It.IsAny<JsonElement>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult
            {
                IsValid = false,
                Errors = new List<Sorcha.Validator.Core.Models.ValidationError>
                {
                    new Sorcha.Validator.Core.Models.ValidationError
                    {
                        Code = "TX_012",
                        Message = "Payload hash mismatch",
                        Field = "payloadHash"
                    }
                }
            });

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isValid").GetBoolean().Should().BeFalse();
        var errors = result.GetProperty("errors").EnumerateArray().ToList();
        errors.Should().ContainSingle();
        errors[0].GetProperty("code").GetString().Should().Be("TX_012");
    }

    [Fact]
    public async Task ValidateTransaction_WithInvalidSignatures_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = CreateValidTransactionRequest();

        // Structure and payload validations pass
        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<JsonElement>(),
                It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(),
                It.IsAny<DateTimeOffset>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidatePayloadHash(It.IsAny<JsonElement>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        // Signature validation fails
        _mockTransactionValidator
            .Setup(v => v.ValidateSignatures(It.IsAny<List<TransactionSignature>>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult
            {
                IsValid = false,
                Errors = new List<Sorcha.Validator.Core.Models.ValidationError>
                {
                    new Sorcha.Validator.Core.Models.ValidationError
                    {
                        Code = "TX_015",
                        Message = "Signature missing public key",
                        Field = "signatures[0].publicKey"
                    }
                }
            });

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isValid").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTransaction_WhenMemPoolFull_ReturnsConflict()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = CreateValidTransactionRequest();

        // All validations pass
        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<JsonElement>(),
                It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(),
                It.IsAny<DateTimeOffset>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidatePayloadHash(It.IsAny<JsonElement>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidateSignatures(It.IsAny<List<TransactionSignature>>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        // Memory pool is full
        _mockPoolPoller
            .Setup(p => p.SubmitTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isValid").GetBoolean().Should().BeTrue();
        result.GetProperty("added").GetBoolean().Should().BeFalse();
        result.GetProperty("message").GetString().Should().Contain("unverified pool");
    }

    [Fact]
    public async Task ValidateTransaction_WithInternalError_ReturnsInternalServerError()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = CreateValidTransactionRequest();

        // Setup mock to throw exception
        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<JsonElement>(),
                It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(),
                It.IsAny<DateTimeOffset>()))
            .Throws(new InvalidOperationException("Test error"));

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact(Skip = "Behavioural drift on the validate endpoint — SUT no longer calls IDocketWaitingMonitor.RegisterForMonitoring " +
        "after a successful mempool add (Feature 113 mempool refactor moved monitoring to a different layer). " +
        "Tracked under #446.")]
    public async Task ValidateTransaction_WhenAddedToMemPool_RegistersForMonitoring()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = CreateValidTransactionRequest();

        // All validations pass and mempool add succeeds
        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(), It.IsAny<DateTimeOffset>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidatePayloadHash(It.IsAny<JsonElement>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidateSignatures(It.IsAny<List<TransactionSignature>>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockPoolPoller
            .Setup(p => p.SubmitTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockMonitoringRegistry.Verify(
            m => m.RegisterForMonitoring(request.RegisterId),
            Times.Once,
            "RegisterForMonitoring should be called after successful mempool addition");
    }

    [Fact]
    public async Task ValidateTransaction_WhenMemPoolFull_DoesNotRegisterForMonitoring()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = CreateValidTransactionRequest();

        _mockTransactionValidator
            .Setup(v => v.ValidateTransactionStructure(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<string>(),
                It.IsAny<List<TransactionSignature>>(), It.IsAny<DateTimeOffset>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidatePayloadHash(It.IsAny<JsonElement>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockTransactionValidator
            .Setup(v => v.ValidateSignatures(It.IsAny<List<TransactionSignature>>(), It.IsAny<string>()))
            .Returns(new Sorcha.Validator.Core.Models.ValidationResult { IsValid = true });

        _mockPoolPoller
            .Setup(p => p.SubmitTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await client.PostAsJsonAsync("/api/v1/transactions/validate", request);

        // Assert
        _mockMonitoringRegistry.Verify(
            m => m.RegisterForMonitoring(It.IsAny<string>()),
            Times.Never,
            "RegisterForMonitoring should NOT be called when mempool addition fails");
    }

    #endregion

    #region GetMemPoolStats Tests

    [Fact]
    public async Task GetMemPoolStats_WithExistingRegister_ReturnsStats()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "register-1";

        var expectedStats = new MemPoolStats
        {
            RegisterId = registerId,
            TotalTransactions = 10,
            HighPriorityCount = 2,
            NormalPriorityCount = 6,
            LowPriorityCount = 2,
            MaxSize = 100,
            TotalEvictions = 0,
            TotalExpired = 0,
            OldestTransactionTime = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        _mockMemPoolManager
            .Setup(m => m.GetStatsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedStats);

        // Act
        var response = await client.GetAsync($"/api/v1/transactions/mempool/{registerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<MemPoolStats>();
        result.Should().NotBeNull();
        result!.RegisterId.Should().Be(registerId);
        result.TotalTransactions.Should().Be(10);
        result.HighPriorityCount.Should().Be(2);
        result.NormalPriorityCount.Should().Be(6);
        result.LowPriorityCount.Should().Be(2);
    }

    [Fact]
    public async Task GetMemPoolStats_WithNonExistentRegister_ReturnsEmptyStats()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "non-existent-register";

        var emptyStats = new MemPoolStats
        {
            RegisterId = registerId,
            TotalTransactions = 0,
            HighPriorityCount = 0,
            NormalPriorityCount = 0,
            LowPriorityCount = 0,
            MaxSize = 100,
            TotalEvictions = 0,
            TotalExpired = 0
        };

        _mockMemPoolManager
            .Setup(m => m.GetStatsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyStats);

        // Act
        var response = await client.GetAsync($"/api/v1/transactions/mempool/{registerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<MemPoolStats>();
        result.Should().NotBeNull();
        result!.TotalTransactions.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a valid transaction request for testing
    /// </summary>
    private static ValidateTransactionRequest CreateValidTransactionRequest()
    {
        // Note: API expects Base64-encoded byte arrays for cryptographic data
        var publicKeyBytes = System.Text.Encoding.UTF8.GetBytes("public-key-1");
        var signatureBytes = System.Text.Encoding.UTF8.GetBytes("signature-value-1");

        return new ValidateTransactionRequest
        {
            TransactionId = $"tx-{Guid.NewGuid()}",
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "action-1",
            Payload = JsonDocument.Parse("{\"data\":\"test\"}").RootElement,
            PayloadHash = "abc123def456",
            Signatures = new List<SignatureRequest>
            {
                new SignatureRequest
                {
                    PublicKey = Convert.ToBase64String(publicKeyBytes),
                    SignatureValue = Convert.ToBase64String(signatureBytes),
                    Algorithm = "ED25519"
                }
            },
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Priority = TransactionPriority.Normal
        };
    }

    #endregion
}
