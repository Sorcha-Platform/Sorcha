// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Tests.Helpers;

/// <summary>
/// Factory for creating in-memory TenantDbContext instances for testing.
/// Each test gets an isolated database to prevent test pollution.
/// </summary>
public static class InMemoryDbContextFactory
{
    /// <summary>
    /// Creates a new TenantDbContext with an in-memory database.
    /// </summary>
    /// <param name="databaseName">Unique database name for test isolation (defaults to random GUID).</param>
    /// <param name="schema">Schema to use (default: "public").</param>
    /// <returns>Configured TenantDbContext using in-memory database.</returns>
    public static TenantDbContext Create(string? databaseName = null, string schema = "public")
    {
        databaseName ??= Guid.NewGuid().ToString();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(databaseName)
            // The in-memory provider has no transaction support; services that wrap writes in an
            // execution-strategy transaction (e.g. OrgProvisioningService) would otherwise throw.
            // Treat transactions as no-ops here — assertions verify the committed end state.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new TenantDbContext(options, schema);

        // Ensure database is created
        context.Database.EnsureCreated();

        return context;
    }

    /// <summary>
    /// Creates a TenantDbContext with pre-seeded test data.
    /// </summary>
    public static TenantDbContext CreateWithSeedData(string? databaseName = null)
    {
        var context = Create(databaseName);
        SeedTestData(context);
        return context;
    }

    private static void SeedTestData(TenantDbContext context)
    {
        // Add default test organizations
        var testOrg = new Sorcha.Tenant.Service.Models.Organization
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = Sorcha.Tenant.Service.Models.OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Organizations.Add(testOrg);
        context.SaveChanges();
    }
}
