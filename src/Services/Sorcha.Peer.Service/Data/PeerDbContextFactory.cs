// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sorcha.Peer.Service.Data;

/// <summary>
/// Design-time factory for PeerDbContext used by EF Core migrations tooling.
/// Development-only defaults are used when the PEER_DB_CONNECTION environment variable is not set.
/// </summary>
public class PeerDbContextFactory : IDesignTimeDbContextFactory<PeerDbContext>
{
    public PeerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PeerDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("PEER_DB_CONNECTION")
            ?? "Host=localhost;Database=sorcha_peer;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "peer");
            });

        return new PeerDbContext(optionsBuilder.Options);
    }
}
