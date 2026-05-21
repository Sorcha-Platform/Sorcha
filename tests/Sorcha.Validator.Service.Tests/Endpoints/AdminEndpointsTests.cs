// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Auth;
using Sorcha.Validator.Service.Endpoints;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Models;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Validator.Core.Validators;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Peer;
using IBlueprintCache = Sorcha.Validator.Service.Services.Interfaces.IBlueprintCache;
using ITransactionPoolPoller = Sorcha.Validator.Service.Services.Interfaces.ITransactionPoolPoller;
using IValidationEngine = Sorcha.Validator.Service.Services.Interfaces.IValidationEngine;
using IVerifiedTransactionQueue = Sorcha.Validator.Service.Services.Interfaces.IVerifiedTransactionQueue;

namespace Sorcha.Validator.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for AdminEndpoints
/// Tests cover validator orchestration, status monitoring, and pipeline execution.
/// Uses a test authentication handler to bypass JWT validation — auth policy
/// correctness is tested in AuthenticationTests.cs.
/// </summary>
public class AdminEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<IValidatorOrchestrator> _mockOrchestrator;

    public AdminEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _mockOrchestrator = new Mock<IValidatorOrchestrator>();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove all hosted services to prevent background service startup
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

                // Replace JWT authentication with test auth handler
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>("Test", _ => { });

                // Remove all services that need mocking
                RemoveService<IValidatorOrchestrator>(services);
                RemoveService<IMemPoolManager>(services);
                RemoveService<IConsensusEngine>(services);
                RemoveService<IDocketBuilder>(services);
                RemoveService<IGenesisManager>(services);
                RemoveService<ITransactionValidator>(services);
                RemoveService<IHashProvider>(services);
                RemoveService<IWalletServiceClient>(services);
                RemoveService<IRegisterServiceClient>(services);
                RemoveService<IPeerServiceClient>(services);

                // Add mocks
                services.AddSingleton<IValidatorOrchestrator>(_ => _mockOrchestrator.Object);

                // Add mocks for dependencies
                var mockMemPool = new Mock<IMemPoolManager>();
                var mockConsensusEngine = new Mock<IConsensusEngine>();
                var mockDocketBuilder = new Mock<IDocketBuilder>();
                var mockGenesisManager = new Mock<IGenesisManager>();
                var mockTransactionValidator = new Mock<ITransactionValidator>();
                var mockHashProvider = new Mock<IHashProvider>();
                var mockWalletClient = new Mock<IWalletServiceClient>();
                var mockRegisterClient = new Mock<IRegisterServiceClient>();
                var mockPeerClient = new Mock<IPeerServiceClient>();

                services.AddSingleton<IMemPoolManager>(_ => mockMemPool.Object);
                services.AddScoped<IConsensusEngine>(_ => mockConsensusEngine.Object);
                services.AddScoped<IDocketBuilder>(_ => mockDocketBuilder.Object);
                services.AddScoped<IGenesisManager>(_ => mockGenesisManager.Object);
                services.AddScoped<ITransactionValidator>(_ => mockTransactionValidator.Object);
                services.AddScoped<IHashProvider>(_ => mockHashProvider.Object);
                services.AddScoped<IWalletServiceClient>(_ => mockWalletClient.Object);
                services.AddScoped<IRegisterServiceClient>(_ => mockRegisterClient.Object);
                services.AddScoped<IPeerServiceClient>(_ => mockPeerClient.Object);

                // Add mocks for validation engine dependencies
                RemoveService<IBlueprintCache>(services);
                RemoveService<ITransactionPoolPoller>(services);
                RemoveService<IValidationEngine>(services);
                RemoveService<IVerifiedTransactionQueue>(services);
                services.AddSingleton<IBlueprintCache>(_ => new Mock<IBlueprintCache>().Object);
                services.AddSingleton<ITransactionPoolPoller>(_ => new Mock<ITransactionPoolPoller>().Object);
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

    #region StartValidator Tests

    [Fact]
    public async Task StartValidator_WithValidRegisterId_ReturnsOkAndStartsValidator()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new StartValidatorRequest { RegisterId = "register-1" };

        _mockOrchestrator
            .Setup(o => o.StartValidatorAsync(request.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("registerId").GetString().Should().Be(request.RegisterId);
        result.GetProperty("status").GetString().Should().Be("Started");

        _mockOrchestrator.Verify(
            o => o.StartValidatorAsync(request.RegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartValidator_WithEmptyRegisterId_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new StartValidatorRequest { RegisterId = "" };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("error").GetString().Should().Contain("RegisterId is required");
    }

    [Fact]
    public async Task StartValidator_WhenStartFails_ReturnsInternalServerError()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new StartValidatorRequest { RegisterId = "register-1" };

        _mockOrchestrator
            .Setup(o => o.StartValidatorAsync(request.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("title").GetString().Should().Contain("Failed to start validator");
    }

    #endregion

    #region StopValidator Tests

    [Fact]
    public async Task StopValidator_WithValidRegisterId_ReturnsOkAndStopsValidator()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new StopValidatorRequest
        {
            RegisterId = "register-1",
            PersistMemPool = false
        };

        _mockOrchestrator
            .Setup(o => o.StopValidatorAsync(
                request.RegisterId,
                request.PersistMemPool,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/stop", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("registerId").GetString().Should().Be(request.RegisterId);
        result.GetProperty("status").GetString().Should().Be("Stopped");
        result.GetProperty("memPoolPersisted").GetBoolean().Should().BeFalse();

        _mockOrchestrator.Verify(
            o => o.StopValidatorAsync(
                request.RegisterId,
                request.PersistMemPool,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StopValidator_WithPersistMemPool_PersistsMemPoolState()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new StopValidatorRequest
        {
            RegisterId = "register-1",
            PersistMemPool = true
        };

        _mockOrchestrator
            .Setup(o => o.StopValidatorAsync(
                request.RegisterId,
                request.PersistMemPool,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/stop", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("memPoolPersisted").GetBoolean().Should().BeTrue();

        _mockOrchestrator.Verify(
            o => o.StopValidatorAsync(request.RegisterId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StopValidator_WithEmptyRegisterId_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new StopValidatorRequest { RegisterId = "", PersistMemPool = false };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/stop", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("error").GetString().Should().Contain("RegisterId is required");
    }

    [Fact]
    public async Task StopValidator_WhenStopFails_ReturnsInternalServerError()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new StopValidatorRequest { RegisterId = "register-1", PersistMemPool = false };

        _mockOrchestrator
            .Setup(o => o.StopValidatorAsync(
                request.RegisterId,
                request.PersistMemPool,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/stop", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region GetValidatorStatus Tests

    [Fact]
    public async Task GetValidatorStatus_WithActiveValidator_ReturnsStatus()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "register-1";

        var expectedStatus = new ValidatorStatus
        {
            RegisterId = registerId,
            IsActive = true,
            TransactionsInMemPool = 25,
            DocketsProposed = 10,
            DocketsConfirmed = 8,
            DocketsRejected = 2,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastDocketBuildAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        _mockOrchestrator
            .Setup(o => o.GetValidatorStatusAsync(registerId))
            .ReturnsAsync(expectedStatus);

        // Act
        var response = await client.GetAsync($"/api/admin/validators/{registerId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ValidatorStatus>();
        result.Should().NotBeNull();
        result!.RegisterId.Should().Be(registerId);
        result.IsActive.Should().BeTrue();
        result.TransactionsInMemPool.Should().Be(25);
        result.DocketsProposed.Should().Be(10);
        result.DocketsConfirmed.Should().Be(8);
        result.DocketsRejected.Should().Be(2);
    }

    [Fact]
    public async Task GetValidatorStatus_WithNonExistentValidator_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "non-existent-register";

        _mockOrchestrator
            .Setup(o => o.GetValidatorStatusAsync(registerId))
            .ReturnsAsync((ValidatorStatus?)null);

        // Act
        var response = await client.GetAsync($"/api/admin/validators/{registerId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("error").GetString().Should().Contain("No validator found");
    }

    [Fact]
    public async Task GetValidatorStatus_WithInactiveValidator_ReturnsInactiveStatus()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "register-1";

        var expectedStatus = new ValidatorStatus
        {
            RegisterId = registerId,
            IsActive = false,
            TransactionsInMemPool = 0,
            DocketsProposed = 0,
            DocketsConfirmed = 0,
            DocketsRejected = 0
        };

        _mockOrchestrator
            .Setup(o => o.GetValidatorStatusAsync(registerId))
            .ReturnsAsync(expectedStatus);

        // Act
        var response = await client.GetAsync($"/api/admin/validators/{registerId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ValidatorStatus>();
        result.Should().NotBeNull();
        result!.IsActive.Should().BeFalse();
    }

    #endregion

    #region ProcessValidationPipeline Tests

    [Fact]
    public async Task ProcessValidationPipeline_WithSuccessfulExecution_ReturnsResult()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "register-1";

        var pipelineResult = new PipelineResult
        {
            Docket = new Docket
            {
                DocketId = "docket-1",
                RegisterId = registerId,
                DocketNumber = 5,
                PreviousHash = "prev-hash",
                DocketHash = "docket-hash",
                CreatedAt = DateTimeOffset.UtcNow,
                MerkleRoot = "merkle-root",
                ProposerValidatorId = "validator-1",
                ProposerSignature = new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("pub-key"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("sig-value"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                },
                Transactions = new List<Transaction>
                {
                    CreateTestTransaction("tx-1"),
                    CreateTestTransaction("tx-2")
                },
                Votes = new List<ConsensusVote>(),
                Status = DocketStatus.Confirmed
            },
            ConsensusAchieved = true,
            WrittenToRegister = true,
            Duration = TimeSpan.FromSeconds(2)
        };

        _mockOrchestrator
            .Setup(o => o.ProcessValidationPipelineAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pipelineResult);

        // Act
        var response = await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("docketNumber").GetInt64().Should().Be(5);
        result.GetProperty("consensusAchieved").GetBoolean().Should().BeTrue();
        result.GetProperty("writtenToRegister").GetBoolean().Should().BeTrue();
        result.GetProperty("transactionCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ProcessValidationPipeline_WhenNoDocketBuilt_ReturnsMessageNoDocket()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "register-1";

        _mockOrchestrator
            .Setup(o => o.ProcessValidationPipelineAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PipelineResult?)null);

        // Act
        var response = await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("message").GetString().Should().Contain("No docket was built");
        result.GetProperty("registerId").GetString().Should().Be(registerId);
    }

    [Fact]
    public async Task ProcessValidationPipeline_WithConsensusFailure_ReturnsFailureDetails()
    {
        // Arrange
        var client = _factory.CreateClient();
        var registerId = "register-1";

        var pipelineResult = new PipelineResult
        {
            Docket = new Docket
            {
                DocketId = "docket-1",
                RegisterId = registerId,
                DocketNumber = 5,
                PreviousHash = "prev-hash",
                DocketHash = "docket-hash",
                CreatedAt = DateTimeOffset.UtcNow,
                MerkleRoot = "merkle-root",
                ProposerValidatorId = "validator-1",
                ProposerSignature = new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("pub-key"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("sig-value"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                },
                Transactions = new List<Transaction>(),
                Votes = new List<ConsensusVote>(),
                Status = DocketStatus.Rejected
            },
            ConsensusAchieved = false,
            WrittenToRegister = false,
            Duration = TimeSpan.FromSeconds(5),
            ErrorMessage = "Insufficient validator approvals"
        };

        _mockOrchestrator
            .Setup(o => o.ProcessValidationPipelineAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pipelineResult);

        // Act
        var response = await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("consensusAchieved").GetBoolean().Should().BeFalse();
        result.GetProperty("writtenToRegister").GetBoolean().Should().BeFalse();
        result.GetProperty("errorMessage").GetString().Should().Contain("Insufficient validator approvals");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test transaction
    /// </summary>
    private static Transaction CreateTestTransaction(string transactionId)
    {
        return new Transaction
        {
            TransactionId = transactionId,
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "action-1",
            Payload = JsonDocument.Parse("{\"data\":\"test\"}").RootElement,
            PayloadHash = "abc123",
            Signatures = new List<Signature>
            {
                new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("pub-key"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("sig-value"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = TransactionPriority.Normal
        };
    }

    #endregion
}

/// <summary>
/// Test authentication handler that automatically authenticates as an Administrator.
/// Used by AdminEndpointsTests to bypass JWT validation for endpoint business logic tests.
/// </summary>
internal sealed class TestAdminAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "test-admin"),
            new Claim(ClaimTypes.Name, "test-admin"),
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(TokenClaimConstants.OrgId, "test-org")
        };

        // Spec 136: inject the installation tier audiences (from the host's SorchaAudiences) so
        // platform-tier admin endpoints (RequireAdministrator + RequirePlatformAudience) accept this principal.
        var audiences = Context.RequestServices.GetService<Sorcha.ServiceDefaults.Auth.SorchaAudiences>()
            ?? new Sorcha.ServiceDefaults.Auth.SorchaAudiences(installationName: null);
        foreach (var aud in audiences.All)
        {
            claims.Add(new Claim("aud", aud));
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
