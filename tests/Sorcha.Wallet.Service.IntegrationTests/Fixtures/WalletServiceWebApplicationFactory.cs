// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Sorcha.Testing;

namespace Sorcha.Wallet.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Wallet Service integration tests.
/// Configures test authentication and in-memory services.
/// </summary>
public class WalletServiceWebApplicationFactory : SorchaWebApplicationFactory<Program>
{
    protected override RedisMockMode RedisMockMode => RedisMockMode.Stateful;

    protected override void ConfigureTestConfiguration(
        WebHostBuilderContext context,
        IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Disable PostgreSQL for in-memory testing
            ["ConnectionStrings:WalletDatabase"] = "",
            // Redis connection string (mocked, but config must parse)
            ["ConnectionStrings:redis"] = "localhost:6379",
        });
    }

    protected override void ConfigureTestAuth(TestAuthHandlerOptions options)
    {
        options.DefaultClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user-id"),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Email, "test@sorcha.io"),
            new("organization_id", "test-org-id"),
            new("org_id", "test-org-id"),
        };
        options.DefaultRole = "User";
    }

    /// <summary>
    /// Creates an HttpClient configured for a specific user.
    /// </summary>
    public HttpClient CreateClientForUser(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", userId);
        return client;
    }
}

/// <summary>
/// Collection definition for shared test context.
/// </summary>
[CollectionDefinition("WalletService")]
public class WalletServiceCollection : ICollectionFixture<WalletServiceWebApplicationFactory>
{
}
