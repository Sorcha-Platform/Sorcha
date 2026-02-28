// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Storage.Abstractions;
using Sorcha.Storage.EFCore.Tests.Fixtures;
using Xunit;

namespace Sorcha.Storage.EFCore.Tests;

public class EFCoreServiceExtensionsTests
{
    // -- AddPostgreSqlDbContext (IConfiguration overload) --------------------

    [Fact]
    public void AddPostgreSqlDbContext_MissingConnectionString_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var act = () => services.AddPostgreSqlDbContext<TestDbContext>(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PostgreSQL connection string*");
    }

    [Fact]
    public void AddPostgreSqlDbContext_EmptyConnectionString_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Warm:Relational:ConnectionString"] = "   "
            })
            .Build();

        var act = () => services.AddPostgreSqlDbContext<TestDbContext>(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PostgreSQL connection string*");
    }

    [Fact]
    public void AddPostgreSqlDbContext_WithConfiguration_RegistersDbContext()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Warm:Relational:ConnectionString"] = "Host=localhost;Database=test"
            })
            .Build();

        var result = services.AddPostgreSqlDbContext<TestDbContext>(config);

        result.Should().BeSameAs(services);
        services.Should().Contain(sd => sd.ServiceType == typeof(TestDbContext));
    }

    // -- AddPostgreSqlDbContext (connection string overload) -----------------

    [Fact]
    public void AddPostgreSqlDbContext_WithConnectionString_RegistersDbContext()
    {
        var services = new ServiceCollection();

        var result = services.AddPostgreSqlDbContext<TestDbContext>(
            "Host=localhost;Database=test");

        result.Should().BeSameAs(services);
        services.Should().Contain(sd => sd.ServiceType == typeof(TestDbContext));
    }

    // -- AddEFCoreRepository ------------------------------------------------

    [Fact]
    public void AddEFCoreRepository_RegistersIRepository()
    {
        var services = new ServiceCollection();

        var result = services.AddEFCoreRepository<TestEntity, Guid, TestDbContext>(e => e.Id);

        result.Should().BeSameAs(services);
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IRepository<TestEntity, Guid>) &&
            sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddEFCoreRepository_ResolvesFromServiceProvider()
    {
        var services = new ServiceCollection();

        // Register InMemory DbContext so the repository can resolve it
        services.AddDbContext<TestDbContext>(options =>
            options.UseInMemoryDatabase(nameof(AddEFCoreRepository_ResolvesFromServiceProvider)));

        services.AddEFCoreRepository<TestEntity, Guid, TestDbContext>(e => e.Id);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetService<IRepository<TestEntity, Guid>>();

        repository.Should().NotBeNull();
        repository.Should().BeOfType<EFCoreRepository<TestEntity, Guid, TestDbContext>>();
    }
}
