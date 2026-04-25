// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Default <see cref="IStorageRegistrationLog"/> implementation.
/// Thread-safe singleton — accumulates registration records over the lifetime
/// of the service and exposes an immutable snapshot to callers.
/// </summary>
public sealed class StorageRegistrationLog : IStorageRegistrationLog
{
    private readonly ILogger<StorageRegistrationLog> _logger;
    private readonly object _lock = new();
    private readonly List<StorageRegistrationRecord> _records = new();
    private readonly HashSet<string> _seenInterfaces = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a new registration log. Typically resolved from DI; only
    /// constructed manually in tests.
    /// </summary>
    public StorageRegistrationLog(ILogger<StorageRegistrationLog> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public void RegisterPersistent(string interfaceName, string implementationName, string backend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);

        var record = Add(new StorageRegistrationRecord(
            InterfaceName: interfaceName,
            ImplementationName: implementationName,
            Backend: backend,
            Reason: $"persistent backend: {backend}",
            RegisteredAt: DateTimeOffset.UtcNow,
            IsAudited: AuditedStorageInterfaces.Names.Contains(interfaceName),
            IsInMemory: false));

        _logger.LogInformation(
            "Storage registration: {Interface} → {Implementation} ({Backend})",
            record.InterfaceName, record.ImplementationName, record.Backend);
    }

    /// <inheritdoc />
    public void RegisterInMemory(string interfaceName, string implementationName, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var record = Add(new StorageRegistrationRecord(
            InterfaceName: interfaceName,
            ImplementationName: implementationName,
            Backend: AuditedStorageInterfaces.InMemoryBackend,
            Reason: reason,
            RegisteredAt: DateTimeOffset.UtcNow,
            IsAudited: AuditedStorageInterfaces.Names.Contains(interfaceName),
            IsInMemory: true));

        // Greppable banner — tooling and operators search for [STORAGE-FALLBACK] in logs.
        _logger.LogWarning(
            "[STORAGE-FALLBACK] {Interface} → {Implementation} — DATA WILL NOT SURVIVE RESTART. Reason: {Reason}",
            record.InterfaceName, record.ImplementationName, record.Reason);
    }

    /// <inheritdoc />
    public IReadOnlyList<StorageRegistrationRecord> Snapshot()
    {
        lock (_lock)
        {
            return _records.ToArray();
        }
    }

    private StorageRegistrationRecord Add(StorageRegistrationRecord record)
    {
        lock (_lock)
        {
            if (!_seenInterfaces.Add(record.InterfaceName))
            {
                throw new InvalidOperationException(
                    $"Storage interface '{record.InterfaceName}' is already registered. " +
                    "Each interface may only be registered once per service. " +
                    "If this is a legitimate re-registration scenario, use a separate interface.");
            }

            _records.Add(record);
        }

        return record;
    }
}
