// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Storage;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Extensions;

/// <summary>
/// Extension methods for registering verified transaction queue services.
/// </summary>
public static class VerifiedQueueExtensions
{
    /// <summary>
    /// Adds the verified transaction queue and related services to the service collection.
    /// PR 7 ships only the in-memory implementation; the Redis-backed implementation
    /// is the next PR.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddVerifiedTransactionQueue(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration
        services.Configure<VerifiedQueueConfiguration>(
            configuration.GetSection(VerifiedQueueConfiguration.SectionName));

        // Register the in-memory queue and record the choice with the storage
        // registration log. IVerifiedTransactionQueue is on the audited list —
        // Production/Staging fail-fast fires when on this in-memory implementation
        // (unless Storage:AllowInMemoryInProduction=true).
        services.AddSingleton<IVerifiedTransactionQueue, InMemoryVerifiedTransactionQueue>();
        services.GetStorageRegistrationLog().RegisterInMemory(
            typeof(IVerifiedTransactionQueue).FullName!,
            typeof(InMemoryVerifiedTransactionQueue).FullName!,
            "Redis-backed mempool not yet wired (PR 8 of feature 113). Mempool will not survive validator restart.");

        // Register cleanup background service
        services.AddHostedService<VerifiedQueueCleanupService>();

        return services;
    }
}
