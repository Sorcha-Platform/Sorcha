// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.User.Pairing;

/// <summary>
/// Mints the enrolment-session artefacts the pairing surfaces hand to the citizen's phone
/// (Features 126 / 128): the QR-bearing enrol-session token, and the 6-digit short code used
/// when the citizen is already on the device they want to pair.
/// </summary>
/// <remarks>
/// <para>
/// Both endpoints are <b>authenticated</b> — they mint a session for the calling citizen. This is a
/// typed client rather than a raw <see cref="HttpClient"/> injection precisely because a host's
/// ambient <see cref="HttpClient"/> makes no promise about carrying the caller's bearer token: in
/// <c>Sorcha.UI.Web.Client</c> the ambient client is deliberately <b>anonymous</b> (it is the one
/// <c>AuthenticationService</c> itself uses, so it cannot depend on the auth handler without a
/// circular dependency). A component that reached for the ambient client therefore sent these
/// requests with no <c>Authorization</c> header and got a 401 — which the citizen saw as
/// "Couldn't generate a pairing code."
/// </para>
/// <para>
/// So the host registers this typed client with whatever auth handler it already uses
/// (<c>AuthenticatedHttpMessageHandler</c> in the web client, the bearer chain in the PWA) — the
/// same contract <see cref="Devices.IHasPairedDeviceProbe"/> follows.
/// </para>
/// </remarks>
public interface IPairingClient
{
    /// <summary>
    /// Mints an enrol-session token for the signed-in citizen. Returns null when the mint fails
    /// (the caller renders its own message — this seam never throws).
    /// </summary>
    /// <param name="mode">
    /// <c>"gated"</c> (the F126 council-page default) or <c>"standalone"</c> (the F128 routes that
    /// live outside a council page). Null sends no mode, preserving the F126 default.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<EnrolSessionMint?> MintAsync(string? mode = null, CancellationToken ct = default);

    /// <summary>
    /// Mints a 6-digit pairing short code wrapping a standalone enrol-session. Returns null when the
    /// mint fails — the short code is a fallback affordance, so callers degrade rather than error.
    /// </summary>
    /// <param name="route">Attribution tag for telemetry (e.g. <c>"MobilewebHandoff"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PairingShortCode?> MintShortCodeAsync(string route, CancellationToken ct = default);

    /// <summary>
    /// Sends the citizen a "finish pairing on your phone" magic link, to their account email.
    /// Returns true when the dispatch was accepted.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> SendPairingResumptionEmailAsync(CancellationToken ct = default);
}

/// <summary>A minted enrol-session: the QR target the phone scans, and when it lapses.</summary>
public sealed record EnrolSessionMint(string SessionToken, string QrUrl, DateTimeOffset ExpiresAt, string? Mode);

/// <summary>A minted 6-digit pairing short code and its expiry.</summary>
public sealed record PairingShortCode(string Code, DateTimeOffset ExpiresAt);

/// <inheritdoc cref="IPairingClient"/>
public sealed class PairingClient : IPairingClient
{
    private const string MintEndpoint = "/api/auth/enrol-session";
    private const string ShortCodeEndpoint = "/api/auth/enrol-session/short-code";
    private const string ResumptionEmailEndpoint = "/api/auth/pairing-resumption-email";

    private readonly HttpClient _http;
    private readonly ILogger<PairingClient> _logger;

    /// <summary>Initialises a new <see cref="EnrolSessionClient"/>.</summary>
    public PairingClient(HttpClient http, ILogger<PairingClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EnrolSessionMint?> MintAsync(string? mode = null, CancellationToken ct = default)
    {
        try
        {
            object body = mode is null ? new { } : new { mode };
            using var response = await _http.PostAsJsonAsync(MintEndpoint, body, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("enrol-session mint returned {Status}", response.StatusCode);
                return null;
            }

            return await response.Content
                .ReadFromJsonAsync<EnrolSessionMint>(JsonDefaults.Api, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "enrol-session mint failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PairingShortCode?> MintShortCodeAsync(string route, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http
                .PostAsJsonAsync(ShortCodeEndpoint, new { route }, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("short-code mint returned {Status}", response.StatusCode);
                return null;
            }

            return await response.Content
                .ReadFromJsonAsync<PairingShortCode>(JsonDefaults.Api, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "short-code mint failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendPairingResumptionEmailAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http
                .PostAsync(ResumptionEmailEndpoint, content: null, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("pairing-resumption email returned {Status}", response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "pairing-resumption email failed");
            return false;
        }
    }
}
