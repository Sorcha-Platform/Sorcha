// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// PWA-side client for the wallet service's pending-application notice surface
/// (Feature 124). Reads via <c>GET</c>, sets via <c>PUT</c>, clears via
/// <c>DELETE</c> on <c>/api/v1/wallet/pending-applications</c>. The wallet's
/// existing <see cref="BearerTokenHandler"/> chain injects the citizen JWT;
/// callers do not pass a token explicitly.
/// </summary>
public interface IPendingApplicationClient
{
    /// <summary>Returns the currently set notice, or null when absent.</summary>
    Task<PendingApplicationView?> GetAsync(CancellationToken ct = default);

    /// <summary>Sets (or replaces) the notice with the supplied label.</summary>
    Task<PendingApplicationView> SetAsync(string label, CancellationToken ct = default);

    /// <summary>Clears the notice. Idempotent.</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>Wallet PWA's view of a pending-application notice.</summary>
/// <param name="Label">Human-readable application label.</param>
/// <param name="SetAt">UTC time the notice was set.</param>
public sealed record PendingApplicationView(string Label, DateTimeOffset SetAt);

/// <summary>
/// Default <see cref="IPendingApplicationClient"/> implementation. Uses the
/// PWA's shared <see cref="HttpClient"/> (which carries the BearerTokenHandler
/// + ServerClockHandler chain).
/// </summary>
public sealed class HttpPendingApplicationClient : IPendingApplicationClient
{
    private const string Path = "/api/v1/wallet/pending-applications";

    private readonly HttpClient _http;

    /// <summary>Initialises a new instance.</summary>
    public HttpPendingApplicationClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public async Task<PendingApplicationView?> GetAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(Path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<PendingApplicationEnvelope>(ct).ConfigureAwait(false);
        return envelope?.Notice;
    }

    /// <inheritdoc />
    public async Task<PendingApplicationView> SetAsync(string label, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync(
            Path,
            new SetPendingApplicationRequest(label),
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<PendingApplicationEnvelope>(ct).ConfigureAwait(false);
        return envelope?.Notice
            ?? throw new InvalidOperationException("Wallet service returned a SetPendingApplication response with no notice.");
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync(Path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent) return;
        response.EnsureSuccessStatusCode();
    }

    private sealed record PendingApplicationEnvelope(PendingApplicationView? Notice);
    private sealed record SetPendingApplicationRequest(string Label);
}
