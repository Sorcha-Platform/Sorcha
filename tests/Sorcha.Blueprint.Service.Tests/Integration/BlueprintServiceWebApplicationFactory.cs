// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AuthenticationOptions = Microsoft.AspNetCore.Authentication.AuthenticationOptions;
using Moq;
using Moq.Protected;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using StackExchange.Redis;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory for integration testing that configures
/// in-memory services and mock HTTP clients
/// </summary>
public class BlueprintServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<HttpMessageHandler> MockWalletHttpHandler { get; } = new();
    public Mock<HttpMessageHandler> MockRegisterHttpHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Add test configuration for Redis connection string
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Add test configuration with Redis connection string to satisfy Aspire requirements
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:redis"] = "localhost:6379,abortConnect=false"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the real Redis output caching
            services.RemoveAll<IDistributedCache>();

            // Add in-memory distributed cache
            services.AddDistributedMemoryCache();

            // Remove Redis-based output cache store and replace with no-op
            services.RemoveAll<IOutputCacheStore>();
            services.AddSingleton<IOutputCacheStore, NoOpOutputCacheStore>();

            // Remove Redis connection multiplexer and replace with mock
            // (required by EventsHubNotificationBridge and other Redis-dependent services)
            services.RemoveAll<IConnectionMultiplexer>();
            var mockMultiplexer = new Mock<IConnectionMultiplexer>();
            var mockSubscriber = new Mock<ISubscriber>();
            mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);
            services.AddSingleton<IConnectionMultiplexer>(mockMultiplexer.Object);

            // Remove the real HTTP clients for Wallet and Register services
            // and add mock implementations
            RemoveHttpClientServices(services);
            AddMockHttpClients(services);

            // Remove existing authentication to replace with test authentication
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.RemoveAll<IPostConfigureOptions<AuthenticationOptions>>();

            // Configure test authentication to bypass JWT validation
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TestScheme";
                options.DefaultChallengeScheme = "TestScheme";
                options.DefaultScheme = "TestScheme";
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("TestScheme", _ => { });
        });

        builder.UseEnvironment("Testing");
    }

    private void RemoveHttpClientServices(IServiceCollection services)
    {
        // Remove existing HttpClient registrations for our service clients
        var walletClientDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IWalletServiceClient));
        if (walletClientDescriptor != null)
        {
            services.Remove(walletClientDescriptor);
        }

        var registerClientDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IRegisterServiceClient));
        if (registerClientDescriptor != null)
        {
            services.Remove(registerClientDescriptor);
        }

        var participantClientDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IParticipantServiceClient));
        if (participantClientDescriptor != null)
        {
            services.Remove(participantClientDescriptor);
        }
    }

    private void AddMockHttpClients(IServiceCollection services)
    {
        // Setup default mock responses for wallet service
        SetupDefaultWalletResponses();

        // Setup default mock responses for register service
        SetupDefaultRegisterResponses();

        // Create mock wallet service client
        var mockWalletClient = new Mock<IWalletServiceClient>();
        mockWalletClient
            .Setup(x => x.EncryptPayloadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wallet, byte[] data, CancellationToken _) =>
                Encoding.UTF8.GetBytes($"encrypted-for-{wallet}"));

        mockWalletClient
            .Setup(x => x.DecryptPayloadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wallet, byte[] data, CancellationToken _) => data);

        mockWalletClient
            .Setup(x => x.GetWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string address, CancellationToken _) => new WalletInfo
            {
                Address = address,
                Name = "Test Wallet",
                PublicKey = "test-public-key",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "test-owner",
                Tenant = "test-tenant",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow
            });

        // Mock VerifySignatureAsync - required for action submission signature verification
        mockWalletClient
            .Setup(x => x.VerifySignatureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock SignTransactionAsync - required for action submission
        mockWalletClient
            .Setup(x => x.SignTransactionAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wallet, byte[] data, string? derivationPath, bool prehashed, CancellationToken _) =>
                new WalletSignResult
                {
                    Signature = Encoding.UTF8.GetBytes("test-signature"),
                    PublicKey = Encoding.UTF8.GetBytes("test-public-key"),
                    SignedBy = wallet,
                    Algorithm = "ED25519"
                });

        // Create mock register service client
        var mockRegisterClient = new Mock<IRegisterServiceClient>();
        mockRegisterClient
            .Setup(x => x.SubmitTransactionAsync(It.IsAny<string>(), It.IsAny<Sorcha.Register.Models.TransactionModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string regId, Sorcha.Register.Models.TransactionModel tx, CancellationToken _) =>
            {
                tx.TxId ??= Guid.NewGuid().ToString();
                return tx;
            });

        mockRegisterClient
            .Setup(x => x.GetTransactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string regId, string txId, CancellationToken _) => new Sorcha.Register.Models.TransactionModel
            {
                TxId = txId,
                RegisterId = regId,
                SenderWallet = "wallet-test",
                TimeStamp = DateTime.UtcNow
            });

        mockRegisterClient
            .Setup(x => x.GetRegisterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string regId, CancellationToken _) => new Sorcha.Register.Models.Register
            {
                Id = regId,
                Name = "Test Register",
                TenantId = "test-tenant",
                Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
            });

        // Create mock participant service client
        var mockParticipantClient = new Mock<IParticipantServiceClient>();
        mockParticipantClient
            .Setup(x => x.GetByUserAndOrgAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                DisplayName = "Test Participant",
                Email = "test@example.com",
                Status = "Active"
            });

        mockParticipantClient
            .Setup(x => x.GetLinkedWalletsAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LinkedWalletInfo>
            {
                new LinkedWalletInfo
                {
                    Id = Guid.NewGuid(),
                    WalletAddress = "0x1234567890abcdef",
                    Algorithm = "ED25519",
                    Status = "Active"
                }
            });

        mockParticipantClient
            .Setup(x => x.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                DisplayName = "Test Participant",
                Email = "test@example.com",
                Status = "Active"
            });

        // Register mock clients
        services.AddSingleton<IWalletServiceClient>(mockWalletClient.Object);
        services.AddSingleton<IRegisterServiceClient>(mockRegisterClient.Object);
        services.AddSingleton<IParticipantServiceClient>(mockParticipantClient.Object);
    }

    private void SetupDefaultWalletResponses()
    {
        // Default encrypt response
        MockWalletHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.RequestUri.PathAndQuery.Contains("/encrypt")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var response = new
                {
                    EncryptedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("encrypted-test-data")),
                    RecipientAddress = "wallet-test",
                    EncryptedAt = DateTime.UtcNow
                };
                return CreateJsonResponse(response);
            });

        // Default decrypt response
        MockWalletHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.RequestUri.PathAndQuery.Contains("/decrypt")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var response = new
                {
                    DecryptedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")),
                    DecryptedBy = "wallet-test",
                    DecryptedAt = DateTime.UtcNow
                };
                return CreateJsonResponse(response);
            });

        // Default sign response
        MockWalletHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.RequestUri.PathAndQuery.Contains("/sign")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var response = new
                {
                    Signature = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-signature")),
                    SignedBy = "wallet-test",
                    SignedAt = DateTime.UtcNow
                };
                return CreateJsonResponse(response);
            });

        // Default get wallet response
        MockWalletHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.Method == HttpMethod.Get &&
                    req.RequestUri.PathAndQuery.Contains("/wallets/")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var response = new WalletInfo
                {
                    Address = "wallet-test",
                    Name = "Test Wallet",
                    PublicKey = "test-public-key",
                    Algorithm = "ED25519",
                    Status = "Active",
                    Owner = "test-owner",
                    Tenant = "test-tenant",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow
                };
                return CreateJsonResponse(response);
            });
    }

    private void SetupDefaultRegisterResponses()
    {
        // Default submit transaction response
        MockRegisterHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.Method == HttpMethod.Post &&
                    req.RequestUri.PathAndQuery.Contains("/transactions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var response = new Sorcha.Register.Models.TransactionModel
                {
                    TxId = Guid.NewGuid().ToString(),
                    RegisterId = "test-register",
                    SenderWallet = "wallet-test",
                    TimeStamp = DateTime.UtcNow
                };
                return CreateJsonResponse(response, HttpStatusCode.Created);
            });

        // Default get transaction response
        MockRegisterHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.Method == HttpMethod.Get &&
                    req.RequestUri.PathAndQuery.Contains("/transactions/")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var response = new Sorcha.Register.Models.TransactionModel
                {
                    TxId = "tx-123",
                    RegisterId = "test-register",
                    SenderWallet = "wallet-test",
                    TimeStamp = DateTime.UtcNow
                };
                return CreateJsonResponse(response);
            });

        // Default get register response
        MockRegisterHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.Method == HttpMethod.Get &&
                    req.RequestUri.PathAndQuery.Contains("/registers/")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var response = new Sorcha.Register.Models.Register
                {
                    Id = "test-register",
                    Name = "Test Register",
                    TenantId = "test-tenant",
                    Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
                };
                return CreateJsonResponse(response);
            });
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>
/// No-op output cache store for testing that doesn't require Redis
/// </summary>
internal class NoOpOutputCacheStore : IOutputCacheStore
{
    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<byte[]?>(null);
    }

    public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Test authentication handler that authenticates all requests with a test user.
/// </summary>
internal class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000123"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim("org_id", "00000000-0000-0000-0000-000000000456"),
            new Claim("tenant_id", "test-tenant-789"),
            new Claim("can_publish_blueprint", "true"),
            new Claim("token_type", "service")
        };

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
