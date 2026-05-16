// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.Wallet.Pwa.Services.Enrolment;

/// <summary>
/// HTTP implementation of <see cref="IPairingShortCodeRedeemer"/>. Typed
/// HttpClient — no bearer-token handler is required (the short code is
/// the credential for this single call).
/// </summary>
public sealed class PairingShortCodeRedeemer : IPairingShortCodeRedeemer
{
    private readonly HttpClient _http;
    private readonly ILogger<PairingShortCodeRedeemer> _logger;

    public PairingShortCodeRedeemer(HttpClient http, ILogger<PairingShortCodeRedeemer> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PairingShortCodeRedeemResult> RedeemAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return PairingShortCodeRedeemResult.Fail(
                PairingShortCodeRedeemErrorCode.MalformedCode,
                "Enter a 6-digit pairing code.");
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "/api/auth/enrol-session/redeem-short-code",
                new RedeemRequest(code),
                ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<RedeemSuccessShape>(cancellationToken: ct)
                    .ConfigureAwait(false);
                if (body is null || string.IsNullOrEmpty(body.AccessToken))
                {
                    return PairingShortCodeRedeemResult.Fail(
                        PairingShortCodeRedeemErrorCode.MalformedCode,
                        "Response missing access token.");
                }
                return PairingShortCodeRedeemResult.Ok(
                    body.AccessToken,
                    body.ExpiresIn,
                    body.DisplayName ?? "",
                    body.Email ?? "");
            }

            var error = await TryReadErrorAsync(response, ct).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => PairingShortCodeRedeemResult.Fail(
                    PairingShortCodeRedeemErrorCode.AlreadyUsedCode,
                    error?.Message ?? "This pairing code has already been used."),
                HttpStatusCode.Gone => PairingShortCodeRedeemResult.Fail(
                    PairingShortCodeRedeemErrorCode.ExpiredCode,
                    error?.Message ?? "This pairing code has expired."),
                HttpStatusCode.TooManyRequests => PairingShortCodeRedeemResult.Fail(
                    PairingShortCodeRedeemErrorCode.RateLimited,
                    error?.Message ?? "Too many attempts. Request a new pairing code."),
                _ => PairingShortCodeRedeemResult.Fail(
                    PairingShortCodeRedeemErrorCode.MalformedCode,
                    error?.Message ?? "That pairing code didn't work."),
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error redeeming pairing short code");
            return PairingShortCodeRedeemResult.Fail(
                PairingShortCodeRedeemErrorCode.Network,
                "Couldn't reach Sorcha.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return PairingShortCodeRedeemResult.Fail(
                PairingShortCodeRedeemErrorCode.Network,
                "Couldn't reach Sorcha.");
        }
    }

    private static async Task<ErrorShape?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorShape>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private sealed record RedeemRequest(string Code);
    private sealed record RedeemSuccessShape(string AccessToken, int ExpiresIn, string? DisplayName, string? Email);
    private sealed record ErrorShape(string? Code, string? Message);
}
