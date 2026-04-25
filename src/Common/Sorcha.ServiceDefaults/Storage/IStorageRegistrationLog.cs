// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Records every storage interface registration at service startup so that
/// Production/Staging deployments can fail fast when an audited interface
/// falls through to an in-memory implementation, and so that operators can
/// observe storage-provider state via OpenTelemetry metrics.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton via <c>AddStorageRegistration</c>. Resolved
/// during each service's storage wiring and called immediately before the
/// matching <c>AddScoped</c> / <c>AddSingleton</c> / <c>AddTransient</c> so
/// the log captures the final selection.
/// </para>
/// <para>
/// Feature 113 — see <c>specs/113-storage-durability-audit/</c>.
/// </para>
/// </remarks>
public interface IStorageRegistrationLog
{
    /// <summary>
    /// Records a persistent-backed registration. Logs at Information.
    /// </summary>
    /// <param name="interfaceName">Fully-qualified interface name.</param>
    /// <param name="implementationName">Fully-qualified implementation class name.</param>
    /// <param name="backend">Persistent backend label, e.g. <c>postgres</c>, <c>mongo</c>, <c>redis</c>.</param>
    /// <exception cref="InvalidOperationException">Thrown if the interface has already been registered in this service.</exception>
    void RegisterPersistent(string interfaceName, string implementationName, string backend);

    /// <summary>
    /// Records an in-memory fallback registration. Logs at Warning with the
    /// <c>[STORAGE-FALLBACK]</c> banner. If the interface is in the audited
    /// set, the enforcement helper will refuse startup in Production/Staging
    /// unless <c>Storage:AllowInMemoryInProduction</c> is true.
    /// </summary>
    /// <param name="interfaceName">Fully-qualified interface name.</param>
    /// <param name="implementationName">Fully-qualified implementation class name.</param>
    /// <param name="reason">Free-text rationale, e.g. "no Postgres connection string configured".</param>
    /// <exception cref="InvalidOperationException">Thrown if the interface has already been registered in this service.</exception>
    void RegisterInMemory(string interfaceName, string implementationName, string reason);

    /// <summary>
    /// Returns an immutable snapshot of all registrations made so far. Used by
    /// the health check, the metrics gauge callbacks, and the enforcement
    /// helper.
    /// </summary>
    IReadOnlyList<StorageRegistrationRecord> Snapshot();
}

/// <summary>
/// Immutable record of a single storage-interface registration.
/// </summary>
/// <param name="InterfaceName">Fully-qualified interface name.</param>
/// <param name="ImplementationName">Fully-qualified implementation class name.</param>
/// <param name="Backend">Persistent backend label or the literal <c>in-memory</c>.</param>
/// <param name="Reason">Free-text rationale, especially for in-memory fallbacks.</param>
/// <param name="RegisteredAt">UTC timestamp of the registration call.</param>
/// <param name="IsAudited"><c>true</c> if the interface is in <see cref="AuditedStorageInterfaces.Names"/>.</param>
/// <param name="IsInMemory"><c>true</c> if <see cref="Backend"/> equals <c>in-memory</c>.</param>
public sealed record StorageRegistrationRecord(
    string InterfaceName,
    string ImplementationName,
    string Backend,
    string Reason,
    DateTimeOffset RegisteredAt,
    bool IsAudited,
    bool IsInMemory);
