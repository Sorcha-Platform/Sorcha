// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Signals that the stored wallet session was rejected by the server and could
/// not be refreshed (the token is expired / revoked / has no refresh token).
/// Raised by <see cref="BearerTokenHandler"/> after it clears the dead token;
/// the shell (MainLayout) subscribes and redirects the citizen to <c>/signin</c>
/// rather than leaving the page running against a dead session (a failing hub,
/// empty preferences, repeated 401s in the console).
/// </summary>
/// <remarks>
/// Mirrors the <c>ServerClockHandler → IServerClockObserver</c> seam: an HTTP
/// message handler can't take a UI dependency (NavigationManager) cleanly, so it
/// publishes a signal a UI component consumes. Singleton — one event reaches
/// every subscriber across the single-user WASM app.
/// </remarks>
public interface ISessionExpiryNotifier
{
    /// <summary>Fired when an unrecoverable 401 clears the stored session.</summary>
    event Action? SessionExpired;

    /// <summary>Raises <see cref="SessionExpired"/>.</summary>
    void NotifyExpired();
}

/// <inheritdoc />
public sealed class SessionExpiryNotifier : ISessionExpiryNotifier
{
    /// <inheritdoc />
    public event Action? SessionExpired;

    /// <inheritdoc />
    public void NotifyExpired() => SessionExpired?.Invoke();
}
