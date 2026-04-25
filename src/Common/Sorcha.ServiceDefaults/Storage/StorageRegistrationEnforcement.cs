// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Validates the registration log at the end of service startup. Throws
/// <see cref="InvalidOperationException"/> when the host environment is
/// Production or Staging and any audited interface fell through to an
/// in-memory implementation, unless the operator has opted in to the
/// override via <c>Storage:AllowInMemoryInProduction=true</c>.
/// </summary>
public static class StorageRegistrationEnforcement
{
    /// <summary>
    /// Configuration key for the override flag.
    /// </summary>
    public const string AllowInMemoryConfigKey = "Storage:AllowInMemoryInProduction";

    /// <summary>
    /// Throws if any audited interface is on an in-memory backend in
    /// Production or Staging, unless explicitly bypassed by configuration.
    /// In Development, logs warnings but never throws.
    /// </summary>
    /// <param name="log">The registration log to validate.</param>
    /// <param name="environment">The current host environment.</param>
    /// <param name="allowInMemoryOverride">When true, bypasses the throw and emits a Critical log entry instead.</param>
    /// <param name="logger">Logger used to emit the bypass / startup banner.</param>
    /// <exception cref="InvalidOperationException">Thrown when the production gate fires.</exception>
    public static void EnforcePersistentStorageInProduction(
        IStorageRegistrationLog log,
        IHostEnvironment environment,
        bool allowInMemoryOverride,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        var snapshot = log.Snapshot();
        var auditedInMemory = snapshot
            .Where(r => r.IsInMemory && r.IsAudited)
            .ToArray();

        // Always emit the startup banner so operators can see, in any environment,
        // exactly what backends each audited interface is using.
        EmitStartupBanner(snapshot, environment, logger);

        if (auditedInMemory.Length == 0)
        {
            return;
        }

        var isProductionLike = environment.IsProduction() || environment.IsStaging();
        if (!isProductionLike)
        {
            // Development (or any custom environment) — warnings already logged at registration time.
            return;
        }

        if (allowInMemoryOverride)
        {
            logger.LogCritical(
                "[STORAGE-FALLBACK-OVERRIDE] {Count} audited storage interface(s) on in-memory backends in {Environment} — " +
                "startup is permitted because {ConfigKey}=true. " +
                "Audited offenders: {Offenders}",
                auditedInMemory.Length,
                environment.EnvironmentName,
                AllowInMemoryConfigKey,
                string.Join(", ", auditedInMemory.Select(r => $"{r.InterfaceName}→{r.ImplementationName}")));
            return;
        }

        // Build a multi-line, copy-pasteable error message identifying every offending interface.
        var lines = auditedInMemory
            .Select(r => $"  - {r.InterfaceName} → {r.ImplementationName} (reason: {r.Reason})");
        var message =
            $"Service '{environment.ApplicationName}' refuses to start in {environment.EnvironmentName}: " +
            $"{auditedInMemory.Length} audited storage interface(s) registered with in-memory implementations.\n" +
            string.Join('\n', lines) +
            $"\nSet {AllowInMemoryConfigKey}=true to bypass (not recommended).";

        throw new InvalidOperationException(message);
    }

    private static void EmitStartupBanner(
        IReadOnlyList<StorageRegistrationRecord> snapshot,
        IHostEnvironment environment,
        ILogger logger)
    {
        var persistent = snapshot.Count(r => !r.IsInMemory);
        var inMemory = snapshot.Count(r => r.IsInMemory);
        var auditedInMemory = snapshot.Count(r => r.IsAudited && r.IsInMemory);

        logger.LogInformation(
            "Storage registration summary for {Service} in {Environment}: " +
            "{Persistent} persistent, {InMemory} in-memory ({AuditedInMemory} of which are audited).",
            environment.ApplicationName,
            environment.EnvironmentName,
            persistent,
            inMemory,
            auditedInMemory);
    }
}
