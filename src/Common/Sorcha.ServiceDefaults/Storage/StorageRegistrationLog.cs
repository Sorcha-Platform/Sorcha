// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Default <see cref="IStorageRegistrationLog"/> implementation.
/// Thread-safe singleton — accumulates registration records over the lifetime
/// of the service and exposes an immutable snapshot to callers.
/// </summary>
/// <remarks>
/// Registration calls are pure data capture — they do not log directly.
/// Logging is deferred to <see cref="StorageEnforcementHostedService"/>
/// which iterates the snapshot at host startup using the proper DI logger
/// and emits one Information line per persistent registration plus one
/// Warning line (with the <c>[STORAGE-FALLBACK]</c> banner) per in-memory
/// registration. This decoupling lets services materialise the log at
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>-extension
/// time (before the DI container is built and before any logger is
/// available) without sacrificing log fidelity.
/// </remarks>
public sealed class StorageRegistrationLog : IStorageRegistrationLog
{
    private readonly object _lock = new();
    private readonly List<StorageRegistrationRecord> _records = new();
    private readonly HashSet<string> _seenInterfaces = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void RegisterPersistent(string interfaceName, string implementationName, string backend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);

        Add(new StorageRegistrationRecord(
            InterfaceName: interfaceName,
            ImplementationName: implementationName,
            Backend: backend,
            Reason: $"persistent backend: {backend}",
            RegisteredAt: DateTimeOffset.UtcNow,
            IsAudited: AuditedStorageInterfaces.Names.Contains(interfaceName),
            IsInMemory: false));
    }

    /// <inheritdoc />
    public void RegisterInMemory(string interfaceName, string implementationName, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Add(new StorageRegistrationRecord(
            InterfaceName: interfaceName,
            ImplementationName: implementationName,
            Backend: AuditedStorageInterfaces.InMemoryBackend,
            Reason: reason,
            RegisteredAt: DateTimeOffset.UtcNow,
            IsAudited: AuditedStorageInterfaces.Names.Contains(interfaceName),
            IsInMemory: true));
    }

    /// <inheritdoc />
    public IReadOnlyList<StorageRegistrationRecord> Snapshot()
    {
        lock (_lock)
        {
            return _records.ToArray();
        }
    }

    private void Add(StorageRegistrationRecord record)
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
    }
}
