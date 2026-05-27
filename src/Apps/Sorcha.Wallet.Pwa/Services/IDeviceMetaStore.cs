// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Persists the wallet's enrolment metadata — server-assigned device id,
/// citizen-visible label, and current delegation expiry (Feature 114, T106
/// PWA-side trigger). Lives alongside the device key in the IndexedDB
/// <c>device</c> store as singleton key <c>enrolment</c>; pulled out of
/// <see cref="IDelegationStore"/> so the existing string-only delegation
/// API stays untouched and callers that only need the JWT don't pay for
/// the metadata.
/// </summary>
public interface IDeviceMetaStore
{
    /// <summary>Returns the persisted enrolment metadata, or null if not yet enrolled.</summary>
    Task<DeviceMetaRecord?> GetAsync(CancellationToken ct = default);

    /// <summary>Persist (or replace) the enrolment metadata.</summary>
    Task SetAsync(DeviceMetaRecord record, CancellationToken ct = default);

    /// <summary>Clear the enrolment metadata (e.g. on sign-out).</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>The wallet's enrolment metadata, persisted client-side.</summary>
/// <param name="DeviceId">Server-assigned device id.</param>
/// <param name="Label">Citizen-editable device label.</param>
/// <param name="EnrolledAt">UTC time the device was first enrolled.</param>
/// <param name="DelegationExpiresAt">UTC expiry of the current delegation credential.</param>
public sealed record DeviceMetaRecord(
    Guid DeviceId,
    string Label,
    DateTimeOffset EnrolledAt,
    DateTimeOffset DelegationExpiresAt);

/// <summary>In-memory <see cref="IDeviceMetaStore"/> for tests.</summary>
public sealed class InMemoryDeviceMetaStore : IDeviceMetaStore
{
    private DeviceMetaRecord? _record;
    /// <inheritdoc />
    public Task<DeviceMetaRecord?> GetAsync(CancellationToken ct = default) => Task.FromResult(_record);
    /// <inheritdoc />
    public Task SetAsync(DeviceMetaRecord record, CancellationToken ct = default) { _record = record; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default) { _record = null; return Task.CompletedTask; }
}

/// <summary>IndexedDB-backed <see cref="IDeviceMetaStore"/>.</summary>
public sealed class IndexedDbDeviceMetaStore : IDeviceMetaStore
{
    private const string StoreName = "device";
    private const string Key = "enrolment";

    private readonly IJSRuntime _js;
    /// <summary>Initialises a new instance.</summary>
    public IndexedDbDeviceMetaStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<DeviceMetaRecord?> GetAsync(CancellationToken ct = default)
        => await _js.InvokeAsync<DeviceMetaRecord?>("SorchaIndexedDb.get", ct, StoreName, Key);

    /// <inheritdoc />
    public async Task SetAsync(DeviceMetaRecord record, CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, record, Key);

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.del", ct, StoreName, Key);
}
