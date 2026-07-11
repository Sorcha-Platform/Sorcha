// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sorcha.Tenant.Service.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the model without the full application
/// host (which only configures a provider when a runtime connection string is present). Uses the Npgsql
/// provider with a placeholder connection string — the connection is never opened at design time, only
/// the model is read. Not used at runtime.
/// </summary>
public sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    /// <inheritdoc />
    public TenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql("Host=localhost;Database=sorcha_tenant_design;Username=design;Password=design")
            .Options;
        return new TenantDbContext(options);
    }
}
