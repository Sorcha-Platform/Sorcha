// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sorcha.Wallet.Core.Data;

/// <summary>
/// Design-time factory for WalletDbContext.
/// Used by EF Core tools for migrations.
/// </summary>
public class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    /// <summary>
    /// Creates a new instance of <see cref="WalletDbContext"/> for design-time operations such as migrations.
    /// Uses a placeholder PostgreSQL connection string that will be replaced at runtime with actual configuration.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the EF Core tools.</param>
    /// <returns>A configured <see cref="WalletDbContext"/> instance.</returns>
    public WalletDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();

        // Use a placeholder connection string for design-time operations
        // The actual connection string is provided at runtime via configuration
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=sorcha_wallet;Username=postgres;Password=postgres",
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "wallet");
            });

        return new WalletDbContext(optionsBuilder.Options);
    }
}
