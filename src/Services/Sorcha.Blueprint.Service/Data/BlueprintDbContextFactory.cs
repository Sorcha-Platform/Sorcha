// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sorcha.Blueprint.Service.Data;

/// <summary>
/// Design-time factory for BlueprintDbContext.
/// Used by EF Core tools for migrations.
/// </summary>
public class BlueprintDbContextFactory : IDesignTimeDbContextFactory<BlueprintDbContext>
{
    public BlueprintDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BlueprintDbContext>();

        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=sorcha_blueprint;Username=postgres;Password=postgres");

        return new BlueprintDbContext(optionsBuilder.Options);
    }
}
