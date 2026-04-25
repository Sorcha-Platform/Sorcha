// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Tests for <see cref="SorchaConnectionsExtensions"/>: the platform-default + per-service
/// override cascade that resolves Postgres / Mongo / Redis connection strings.
/// </summary>
public class SorchaConnectionsExtensionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // ----- Postgres -----

    [Fact]
    public void GetSorchaPostgresConnectionString_ServiceOverride_WinsOverDefault()
    {
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Postgres"] = "Host=default-pg;Username=sorcha",
            ["ConnectionStrings:Tenant:Postgres"] = "Host=tenant-pg;Username=tenant_user"
        });

        var result = config.GetSorchaPostgresConnectionString("Tenant", "sorcha_tenant");

        result.Should().Be("Host=tenant-pg;Username=tenant_user;Database=sorcha_tenant");
    }

    [Fact]
    public void GetSorchaPostgresConnectionString_NoOverride_FallsBackToDefault()
    {
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Postgres"] = "Host=default-pg;Username=sorcha"
        });

        var result = config.GetSorchaPostgresConnectionString("Tenant", "sorcha_tenant");

        result.Should().Be("Host=default-pg;Username=sorcha;Database=sorcha_tenant");
    }

    [Fact]
    public void GetSorchaPostgresConnectionString_AppendsDatabase_WhenMissing()
    {
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Postgres"] = "Host=postgres;Username=sorcha;Password=p"
        });

        var result = config.GetSorchaPostgresConnectionString("Wallet", "sorcha_wallet");

        result.Should().EndWith(";Database=sorcha_wallet");
    }

    [Fact]
    public void GetSorchaPostgresConnectionString_PreservesDatabase_WhenAlreadyPresent()
    {
        // The override case: an op explicitly set the full string with their own Database name.
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Tenant:Postgres"] = "Host=tenant-pg;Database=custom_db;Username=u"
        });

        var result = config.GetSorchaPostgresConnectionString("Tenant", "sorcha_tenant");

        result.Should().Be("Host=tenant-pg;Database=custom_db;Username=u");
    }

    [Fact]
    public void GetSorchaPostgresConnectionString_HandlesTrailingSemicolonOnBase()
    {
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Postgres"] = "Host=postgres;Username=sorcha;"
        });

        var result = config.GetSorchaPostgresConnectionString("Peer", "sorcha_peer");

        // No double-semicolon
        result.Should().Be("Host=postgres;Username=sorcha;Database=sorcha_peer");
    }

    [Fact]
    public void GetSorchaPostgresConnectionString_NoConfiguration_Throws()
    {
        var config = BuildConfig(new());

        var act = () => config.GetSorchaPostgresConnectionString("Tenant", "sorcha_tenant");

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*ConnectionStrings:Tenant:Postgres*ConnectionStrings:Sorcha:Postgres*");
    }

    // ----- Mongo -----

    [Fact]
    public void GetSorchaMongoConnectionString_ServiceOverride_WinsOverDefault()
    {
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Mongo"] = "mongodb://default-mongo:27017",
            ["ConnectionStrings:Register:Mongo"] = "mongodb://register-mongo:27017"
        });

        var result = config.GetSorchaMongoConnectionString("Register");

        result.Should().Be("mongodb://register-mongo:27017");
    }

    [Fact]
    public void GetSorchaMongoConnectionString_NoOverride_FallsBackToDefault()
    {
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Mongo"] = "mongodb://default-mongo:27017"
        });

        var result = config.GetSorchaMongoConnectionString("Register");

        result.Should().Be("mongodb://default-mongo:27017");
    }

    [Fact]
    public void GetSorchaMongoConnectionString_DoesNotAppendDatabase()
    {
        // Mongo selects the DB via IMongoClient.GetDatabase(name), not in the connection string.
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Mongo"] = "mongodb://user:pw@host:27017"
        });

        var result = config.GetSorchaMongoConnectionString("Register");

        result.Should().NotContain("Database=");
    }

    // ----- Redis -----

    [Fact]
    public void GetSorchaRedisConnectionString_ServiceOverride_WinsOverDefault()
    {
        var config = BuildConfig(new()
        {
            ["ConnectionStrings:Sorcha:Redis"] = "redis:6379",
            ["ConnectionStrings:Tenant:Redis"] = "tenant-redis:6379"
        });

        var result = config.GetSorchaRedisConnectionString("Tenant");

        result.Should().Be("tenant-redis:6379");
    }

    [Fact]
    public void GetSorchaRedisConnectionString_NoConfiguration_Throws()
    {
        var config = BuildConfig(new());

        var act = () => config.GetSorchaRedisConnectionString("Tenant");

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Redis*");
    }

    // ----- Argument validation -----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetSorchaPostgresConnectionString_InvalidServiceName_Throws(string? serviceName)
    {
        var config = BuildConfig(new());
        var act = () => config.GetSorchaPostgresConnectionString(serviceName!, "db");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetSorchaPostgresConnectionString_InvalidDatabaseName_Throws(string? databaseName)
    {
        var config = BuildConfig(new());
        var act = () => config.GetSorchaPostgresConnectionString("Tenant", databaseName!);
        act.Should().Throw<ArgumentException>();
    }
}
