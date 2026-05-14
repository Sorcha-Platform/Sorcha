// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Per-device flags persisted client-side in IndexedDB (Feature 124).
/// Initially carries only the welcome-takeover dismissal record; designed to
/// accept further flags as later specs land. Co-tenants the existing
/// <c>device</c> store alongside <see cref="DeviceMetaRecord"/> at key
/// <c>flags</c>.
/// </summary>
public interface IWalletFlagsStore
{
    /// <summary>Returns the persisted flags, or null if never written.</summary>
    Task<WalletFlagsRecord?> GetAsync(CancellationToken ct = default);

    /// <summary>Persist (or replace) the flags record.</summary>
    Task SetAsync(WalletFlagsRecord record, CancellationToken ct = default);
}

/// <summary>
/// Per-device wallet flags. <see cref="WelcomedAt"/> only ever transitions
/// from null to a UTC timestamp on first dismissal of the welcome takeover;
/// there is no "un-welcome" path on this device.
/// </summary>
/// <param name="WelcomedAt">UTC time the welcome takeover was dismissed.</param>
public sealed record WalletFlagsRecord(
    DateTimeOffset? WelcomedAt);

/// <summary>In-memory <see cref="IWalletFlagsStore"/> for tests.</summary>
public sealed class InMemoryWalletFlagsStore : IWalletFlagsStore
{
    private WalletFlagsRecord? _record;
    /// <inheritdoc />
    public Task<WalletFlagsRecord?> GetAsync(CancellationToken ct = default) => Task.FromResult(_record);
    /// <inheritdoc />
    public Task SetAsync(WalletFlagsRecord record, CancellationToken ct = default)
    {
        _record = record;
        return Task.CompletedTask;
    }
}

/// <summary>IndexedDB-backed <see cref="IWalletFlagsStore"/>.</summary>
public sealed class IndexedDbWalletFlagsStore : IWalletFlagsStore
{
    private const string StoreName = "device";
    private const string Key = "flags";

    private readonly IJSRuntime _js;
    /// <summary>Initialises a new instance.</summary>
    public IndexedDbWalletFlagsStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<WalletFlagsRecord?> GetAsync(CancellationToken ct = default)
        => await _js.InvokeAsync<WalletFlagsRecord?>("SorchaIndexedDb.get", ct, StoreName, Key);

    /// <inheritdoc />
    public async Task SetAsync(WalletFlagsRecord record, CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, record, Key);
}
