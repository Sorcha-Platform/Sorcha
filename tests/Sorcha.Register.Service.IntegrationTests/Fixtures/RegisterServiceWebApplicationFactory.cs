// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Moq;
using Sorcha.Testing;

namespace Sorcha.Register.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Register Service integration tests.
/// Uses in-memory storage and mocked Redis/MongoDB for fast testing.
/// </summary>
public class RegisterServiceWebApplicationFactory : SorchaWebApplicationFactory<Program>
{
    protected override void ConfigureHostBuilder(IWebHostBuilder builder)
    {
        // UseSetting applies configuration before Program.cs builder code runs,
        // which is critical for settings read during the builder phase
        // (e.g. AddSystemWalletSigning).
        builder.UseSetting("RegisterStorage:Type", "InMemory");
        builder.UseSetting("ConnectionStrings:MongoDB", "");
        builder.UseSetting("SystemWalletSigning:ValidatorId", "test-validator-id");
    }

    protected override void ConfigureTestConfiguration(
        WebHostBuilderContext context,
        IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RegisterStorage:Type"] = "InMemory",
            ["ConnectionStrings:MongoDB"] = "",
            ["SystemWalletSigning:ValidatorId"] = "test-validator-id",
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

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // SystemRegisterService now uses RegisterManager/TransactionManager
        // (backed by IRegisterRepository) so no MongoDB mocking is required for
        // the system register; we only provide mock IMongoClient/IMongoDatabase
        // so any remaining DI doesn't fail.
        services.RemoveAll<IMongoClient>();
        services.RemoveAll<IMongoDatabase>();

        var mockMongoDatabase = new Mock<IMongoDatabase>();
        var mockMongoClient = new Mock<IMongoClient>();
        mockMongoClient.Setup(c => c.GetDatabase(It.IsAny<string>(), It.IsAny<MongoDatabaseSettings>()))
            .Returns(mockMongoDatabase.Object);

        services.AddSingleton<IMongoClient>(mockMongoClient.Object);
        services.AddSingleton<IMongoDatabase>(mockMongoDatabase.Object);
    }

    /// <summary>
    /// Creates an HttpClient configured as a service token (for CanWriteDockets policy).
    /// </summary>
    public HttpClient CreateServiceClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        client.DefaultRequestHeaders.Add("X-Test-Token-Type", "service");
        return client;
    }
}

/// <summary>
/// Collection definition for shared test context.
/// </summary>
[CollectionDefinition("RegisterService")]
public class RegisterServiceCollection : ICollectionFixture<RegisterServiceWebApplicationFactory>
{
}
