// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using Serilog;
using Serilog.Events;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using StackExchange.Redis;

namespace Sorcha.Tenant.Service.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory for integration testing of the Tenant Service.
/// Provides in-memory database, mock Redis, and test authentication.
/// </summary>
public class TenantServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    // Unique database name per factory instance to ensure test isolation
    private readonly string _databaseName = $"TenantServiceTests_{Guid.NewGuid():N}";

    /// <summary>
    /// Configure test services to use in-memory database and mock Redis.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide JWT signing configuration so TokenService can generate real tokens
        // for login/token/bootstrap endpoints that don't use the Test auth scheme
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SigningKey"] = "TestSigningKey_MustBeAtLeast32BytesLong!!",
                ["JwtSettings:Issuer"] = "https://tenant.sorcha.io",
                ["JwtSettings:Audience"] = "https://api.sorcha.io",
                ["JwtSettings:InstallationName"] = "test",
                ["JwtSettings:ValidateIssuer"] = "false",
                ["JwtSettings:ValidateAudience"] = "false",
                ["JwtSettings:ValidateLifetime"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Relax antiforgery cookie policy for test server (non-SSL)
            services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(options =>
            {
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None;
            });

            // Remove all EF Core related services to prevent provider conflicts
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<TenantDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true
                         || d.ServiceType.FullName?.Contains("Npgsql") == true
                         || d.ImplementationType?.FullName?.Contains("Npgsql") == true)
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Remove the existing DbContext
            services.RemoveAll<TenantDbContext>();
            services.RemoveAll<DbContextOptions<TenantDbContext>>();

            // Add InMemory DbContext with unique database name for test isolation
            var databaseName = _databaseName;
            services.AddDbContext<TenantDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
                options.EnableSensitiveDataLogging();
            });

            // Remove existing Redis connection
            services.RemoveAll<IConnectionMultiplexer>();

            // Mock Redis connection
            var mockRedis = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();

            mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            mockDatabase.Setup(d => d.KeyDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            mockDatabase.Setup(d => d.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            services.AddSingleton(mockRedis.Object);

            // Remove any existing wallet service client
            services.RemoveAll<IWalletServiceClient>();

            // Mock Wallet Service Client
            var mockWalletClient = new Mock<IWalletServiceClient>();
            mockWalletClient
                .Setup(w => w.GetWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WalletInfo
                {
                    Address = "sorcha1test123",
                    Name = "Test Wallet",
                    PublicKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    Algorithm = "ED25519",
                    Status = "Active",
                    Owner = "test@test.com",
                    Tenant = "default"
                });
            mockWalletClient
                .Setup(w => w.VerifySignatureAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            services.AddSingleton(mockWalletClient.Object);

            // Remove database initializer (we'll seed manually in tests)
            services.RemoveAll<IHostedService>();

            // Add distributed memory cache for OIDC flow state
            services.AddDistributedMemoryCache();

            // Mock OIDC exchange service for endpoint tests
            var mockExchangeService = new Mock<IOidcExchangeService>();
            mockExchangeService
                .Setup(s => s.GenerateAuthorizationUrlAsync(
                    It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OidcInitiateResponse
                {
                    AuthorizationUrl = "https://login.example.com/authorize?response_type=code&client_id=test&state=test-state",
                    State = "test-state"
                });
            mockExchangeService
                .Setup(s => s.ExchangeCodeAsync(
                    "valid-auth-code", "valid-state-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OidcExchangeResult
                {
                    Success = true,
                    OrgId = TestDataSeeder.TestOrganizationId,
                    Claims = new OidcUserClaims
                    {
                        Subject = "oidc-subject-member",
                        Email = "member@test-org.sorcha.io",
                        DisplayName = "Member User",
                        EmailVerified = true
                    }
                });
            mockExchangeService
                .Setup(s => s.ExchangeCodeAsync(
                    It.IsAny<string>(), "invalid-or-tampered-state", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Invalid or expired state parameter."));
            services.RemoveAll<IOidcExchangeService>();
            services.AddSingleton(mockExchangeService.Object);

            // Mock OIDC provisioning service
            var mockProvisioningService = new Mock<IOidcProvisioningService>();
            mockProvisioningService
                .Setup(s => s.CheckDomainRestrictionsAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true); // Domain allowed
            mockProvisioningService
                .Setup(s => s.ProvisionOrMatchUserAsync(
                    It.IsAny<Guid>(), It.IsAny<OidcUserClaims>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid orgId, OidcUserClaims claims, CancellationToken _) =>
                {
                    var user = new UserIdentity
                    {
                        Id = TestDataSeeder.MemberUserId,
                        Email = claims.Email ?? "member@test-org.sorcha.io",
                        DisplayName = claims.DisplayName ?? "Member User",
                        Status = IdentityStatus.Active,
                        Roles = [UserRole.Member],
                        OrganizationId = orgId,
                        ProfileCompleted = true,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    return (user, false);
                });
            mockProvisioningService
                .Setup(s => s.DetermineProfileCompletionAsync(It.IsAny<UserIdentity>()))
                .ReturnsAsync(false); // Profile is complete
            services.RemoveAll<IOidcProvisioningService>();
            services.AddSingleton(mockProvisioningService.Object);

            // Mock email verification service
            var mockEmailVerification = new Mock<IEmailVerificationService>();
            mockEmailVerification
                .Setup(s => s.VerifyTokenAsync("valid-verification-token", It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, (string?)null));
            mockEmailVerification
                .Setup(s => s.VerifyTokenAsync("invalid-or-expired-token", It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "Invalid verification token."));
            mockEmailVerification
                .Setup(s => s.VerifyTokenAsync("expired-verification-token", It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "Verification token has expired."));
            mockEmailVerification
                .Setup(s => s.CanResendAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            mockEmailVerification
                .Setup(s => s.GenerateAndSendVerificationAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("new-verification-token");
            services.RemoveAll<IEmailVerificationService>();
            services.AddSingleton(mockEmailVerification.Object);

            // Mock password policy service
            var mockPasswordPolicy = new Mock<IPasswordPolicyService>();
            mockPasswordPolicy
                .Setup(s => s.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PasswordValidationResult { IsValid = true, Errors = [] });
            mockPasswordPolicy
                .Setup(s => s.ValidateAsync("short", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PasswordValidationResult
                {
                    IsValid = false,
                    Errors = ["Password must be at least 12 characters."]
                });
            mockPasswordPolicy
                .Setup(s => s.ValidateAsync("breachedpass1234", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PasswordValidationResult
                {
                    IsValid = false,
                    Errors = ["This password has appeared in a data breach and cannot be used."]
                });
            services.RemoveAll<IPasswordPolicyService>();
            services.AddSingleton(mockPasswordPolicy.Object);

            // Mock email sender (avoid SMTP in tests)
            var mockEmailSender = new Mock<IEmailSender>();
            mockEmailSender
                .Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(mockEmailSender.Object);

            // Add test authentication scheme
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
        });

        builder.UseEnvironment("Testing");
    }

    /// <summary>
    /// Create an HTTP client with test authentication configured for an admin user.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.AdminUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", TestDataSeeder.TestOrganizationId.ToString());
        return client;
    }

    /// <summary>
    /// Create an HTTP client with test authentication configured for a regular member.
    /// </summary>
    public HttpClient CreateMemberClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Member");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.MemberUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", TestDataSeeder.TestOrganizationId.ToString());
        return client;
    }

    /// <summary>
    /// Create an unauthenticated HTTP client.
    /// </summary>
    public HttpClient CreateUnauthenticatedClient()
    {
        return CreateClient();
    }

    /// <summary>
    /// Seed test data into the database.
    /// </summary>
    public async Task SeedTestDataAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        await TestDataSeeder.SeedAsync(context);
    }
}
