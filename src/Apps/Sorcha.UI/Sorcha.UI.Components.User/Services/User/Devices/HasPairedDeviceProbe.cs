// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.UI.Core.Services.User.Devices;

/// <summary>
/// HTTP implementation of <see cref="IHasPairedDeviceProbe"/>. Typed
/// HttpClient — base address is the gateway origin, no bearer-token handler
/// is wired in this constructor (the caller registers the typed client with
/// its existing auth handlers).
/// </summary>
public sealed class HasPairedDeviceProbe : IHasPairedDeviceProbe
{
    private const string Endpoint = "/api/v1/me/devices/has-any";

    private readonly HttpClient _http;
    private readonly ILogger<HasPairedDeviceProbe> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _initialFetchAttempted;

    public HasPairedDeviceProbe(HttpClient http, ILogger<HasPairedDeviceProbe> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool? HasAnyDevice { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? LatestEnrolledAt { get; private set; }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_initialFetchAttempted)
        {
            return;
        }

        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialFetchAttempted)
            {
                return;
            }

            await FetchOnceAsync(ct).ConfigureAwait(false);
            _initialFetchAttempted = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FetchOnceAsync(ct).ConfigureAwait(false);
            _initialFetchAttempted = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RaiseLocalPairCompleted(CancellationToken ct = default)
    {
        // Optimistic local update — flip the cached value before the network
        // round-trip so the takeover dismisses immediately. The subsequent
        // RefreshAsync reconciles against server truth.
        var changed = HasAnyDevice != true;
        HasAnyDevice = true;
        LatestEnrolledAt ??= DateTimeOffset.UtcNow;
        if (changed)
        {
            Changed?.Invoke();
        }

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    private async Task FetchOnceAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(Endpoint, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Not signed in — leave value as null so callers treat as
                // "unknown, don't gate" (a signed-out citizen is never in
                // the takeover-or-banner-eligible state anyway).
                _logger.LogDebug("HasPairedDeviceProbe unauthorised — leaving value null");
                return;
            }

            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<HasAnyDeviceResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            if (payload is null)
            {
                _logger.LogWarning("HasPairedDeviceProbe received empty body — leaving cached value unchanged");
                return;
            }

            var hasChanged =
                HasAnyDevice != payload.HasAnyDevice ||
                LatestEnrolledAt != payload.LatestEnrolledAt;

            HasAnyDevice = payload.HasAnyDevice;
            LatestEnrolledAt = payload.LatestEnrolledAt;

            if (hasChanged)
            {
                Changed?.Invoke();
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HasPairedDeviceProbe network error — leaving cached value unchanged");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("HasPairedDeviceProbe timed out — leaving cached value unchanged");
        }
    }
}
