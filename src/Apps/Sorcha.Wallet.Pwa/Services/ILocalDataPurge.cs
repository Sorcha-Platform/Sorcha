// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Wipes every per-device wallet store on sign-out. Sign-out previously cleared
/// only the access token (<see cref="IAccessTokenStore"/>), which left a signed-out
/// citizen's credentials, personas, delegation, presentation/verification history,
/// sync cursor, active-context, and the welcome/guided-tour flags in IndexedDB for
/// whoever signed in next on the same device — a cross-user data leak and the cause
/// of the "welcome wizard won't reappear for a new user" symptom.
/// <para>
/// The implementation clears the whole IndexedDB database by enumerating its object
/// stores, so any store added in future is wiped automatically — no per-store
/// maintenance, which is precisely the fragility that caused the original bug.
/// </para>
/// </summary>
public interface ILocalDataPurge
{
    /// <summary>Clear every per-device wallet store. Idempotent; safe when already empty.</summary>
    Task PurgeAsync(CancellationToken ct = default);
}

/// <summary>
/// Production <see cref="ILocalDataPurge"/> — delegates to the
/// <c>SorchaIndexedDb.wipe</c> bridge function which clears every object store in
/// the <c>sorcha-wallet</c> database in a single transaction.
/// </summary>
public sealed class IndexedDbLocalDataPurge : ILocalDataPurge
{
    private readonly IJSRuntime _js;

    /// <summary>Initialises a new instance.</summary>
    public IndexedDbLocalDataPurge(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task PurgeAsync(CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.wipe", ct);
}
