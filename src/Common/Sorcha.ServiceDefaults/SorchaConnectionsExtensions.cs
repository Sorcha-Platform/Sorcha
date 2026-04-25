// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;

namespace Sorcha.ServiceDefaults;

/// <summary>
/// Connection-string resolution helpers with a Sorcha-platform default + per-service
/// override pattern.
///
/// Lookup order for any (serviceName, kind) pair:
///   1. <c>ConnectionStrings:{serviceName}:{kind}</c> — service-specific override
///   2. <c>ConnectionStrings:Sorcha:{kind}</c> — platform-wide default
///
/// Where <c>{kind}</c> is one of <c>Postgres</c>, <c>Mongo</c>, <c>Redis</c>.
///
/// In environment-variable form (compose, k8s) these become
/// <c>ConnectionStrings__Sorcha__Postgres</c> / <c>ConnectionStrings__Tenant__Postgres</c>
/// respectively.
///
/// Postgres connection strings carry the database name in-band
/// (<c>Database=sorcha_tenant</c>); the resolver appends the per-service database
/// name when the configured base string omits it. Mongo and Redis don't carry
/// database/db-index in the connection string, so the resolver returns those
/// untouched.
/// </summary>
public static class SorchaConnectionsExtensions
{
    private const string DefaultPrefix = "Sorcha";

    /// <summary>
    /// Resolves a Postgres connection string for the given service. Falls back to the
    /// <c>Sorcha</c> platform default if no service-specific override is configured.
    /// Appends <c>Database={databaseName}</c> when the resolved string doesn't already
    /// specify a database (the common case for the cascade default).
    /// </summary>
    public static string GetSorchaPostgresConnectionString(
        this IConfiguration configuration,
        string serviceName,
        string databaseName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var raw = ResolveOrThrow(configuration, serviceName, "Postgres");
        return EnsureDatabase(raw, databaseName);
    }

    /// <summary>
    /// Resolves a Mongo connection string for the given service. The database is
    /// selected separately via <c>IMongoClient.GetDatabase(name)</c>, so this returns
    /// the resolved string as-is.
    /// </summary>
    public static string GetSorchaMongoConnectionString(
        this IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return ResolveOrThrow(configuration, serviceName, "Mongo");
    }

    /// <summary>
    /// Resolves a Redis connection string for the given service. Per-service overrides
    /// are mostly an operational lever (e.g. point one service at a dedicated Redis
    /// instance); the platform default is the common case.
    /// </summary>
    public static string GetSorchaRedisConnectionString(
        this IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return ResolveOrThrow(configuration, serviceName, "Redis");
    }

    private static string ResolveOrThrow(IConfiguration configuration, string serviceName, string kind)
    {
        var serviceKey = $"ConnectionStrings:{serviceName}:{kind}";
        var defaultKey = $"ConnectionStrings:{DefaultPrefix}:{kind}";

        var resolved = configuration[serviceKey] ?? configuration[defaultKey];
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException(
                $"No {kind} connection string configured. Set {serviceKey} (override) " +
                $"or {defaultKey} (platform default).");
        }
        return resolved;
    }

    private static string EnsureDatabase(string connectionString, string databaseName)
    {
        if (connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var trimmed = connectionString.TrimEnd();
        var separator = trimmed.EndsWith(';') ? "" : ";";
        return $"{trimmed}{separator}Database={databaseName}";
    }
}
