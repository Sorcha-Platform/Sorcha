// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Contract reference for feature 113-storage-durability-audit.
// This file is a planning artefact. The implementation lives in
// src/Common/Sorcha.ServiceDefaults/Storage/.

using Microsoft.Extensions.Hosting;

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Records every storage interface registration at service startup. Drives
/// fail-fast in Production/Staging when audited interfaces fall through to
/// in-memory implementations, and feeds the storage-providers health check
/// and the sorcha_storage_provider_info metric.
/// </summary>
/// <remarks>
/// Registered as a singleton in <c>AddServiceDefaults</c>. Resolved during
/// each service's storage wiring and called immediately before
/// <c>AddScoped</c>/<c>AddSingleton</c>/<c>AddTransient</c> so the log
/// captures the final selection.
/// </remarks>
public interface IStorageRegistrationLog
{
    /// <summary>
    /// Records a persistent-backed registration. Logs at Information.
    /// </summary>
    /// <param name="interfaceName">Fully-qualified interface name.</param>
    /// <param name="implementationName">Fully-qualified implementation class name.</param>
    /// <param name="backend">Persistent backend label, e.g. "postgres", "mongo", "redis".</param>
    /// <exception cref="InvalidOperationException">If the interface has already been registered in this service.</exception>
    void RegisterPersistent(string interfaceName, string implementationName, string backend);

    /// <summary>
    /// Records an in-memory fallback registration. Logs at Warning with the
    /// [STORAGE-FALLBACK] banner. If the interface is in the audited set,
    /// the fail-fast helper will refuse startup in Production/Staging unless
    /// Storage:AllowInMemoryInProduction is true.
    /// </summary>
    /// <param name="interfaceName">Fully-qualified interface name.</param>
    /// <param name="implementationName">Fully-qualified implementation class name.</param>
    /// <param name="reason">Free-text rationale, e.g. "no Postgres connection string configured".</param>
    /// <exception cref="InvalidOperationException">If the interface has already been registered in this service.</exception>
    void RegisterInMemory(string interfaceName, string implementationName, string reason);

    /// <summary>
    /// Snapshot of all registrations made so far. Used by the health check,
    /// the metrics gauge, and the fail-fast helper.
    /// </summary>
    IReadOnlyList<StorageRegistrationRecord> Snapshot();
}

/// <summary>
/// Immutable record of a single storage-interface registration. See
/// data-model.md §1.
/// </summary>
public sealed record StorageRegistrationRecord(
    string InterfaceName,
    string ImplementationName,
    string Backend,
    string Reason,
    DateTimeOffset RegisteredAt,
    bool IsAudited,
    bool IsInMemory);

/// <summary>
/// Validates the registration log at the end of service startup. Throws
/// InvalidOperationException with a multi-line message naming every audited
/// interface that fell through to in-memory, unless Storage:AllowInMemoryInProduction
/// is true.
/// </summary>
/// <remarks>
/// Called once via an <c>IHostedService</c> that runs after DI container is
/// built but before the host accepts traffic. Production and Staging both
/// trigger fail-fast; other environments log warnings only.
/// </remarks>
public static class StorageRegistrationEnforcement
{
    /// <summary>
    /// Throws if any audited interface is on an in-memory backend in
    /// Production or Staging, unless explicitly bypassed by configuration.
    /// </summary>
    public static void EnforcePersistentStorageInProduction(
        IStorageRegistrationLog log,
        IHostEnvironment environment,
        bool allowInMemoryOverride);
}
