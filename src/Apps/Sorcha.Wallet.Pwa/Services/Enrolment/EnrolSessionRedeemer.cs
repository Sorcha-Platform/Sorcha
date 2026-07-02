// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.Wallet.Pwa.Services.Enrolment;

/// <summary>
/// HTTP implementation of <see cref="IEnrolSessionRedeemer"/>. Uses a typed
/// HttpClient configured against the API gateway. No bearer-token handler
/// is wired — the session token in the request body is the credential.
/// </summary>
public sealed class EnrolSessionRedeemer : IEnrolSessionRedeemer
{
    private readonly HttpClient _http;
    private readonly ILogger<EnrolSessionRedeemer> _logger;

    public EnrolSessionRedeemer(HttpClient http, ILogger<EnrolSessionRedeemer> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EnrolRedeemResult> RedeemAsync(string sessionToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return EnrolRedeemResult.Fail(EnrolRedeemErrorCode.MalformedToken, "Missing session token.");
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "/api/auth/enrol-session/redeem",
                new RedeemRequest(sessionToken),
                ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<RedeemSuccessShape>(JsonDefaults.Api, ct)
                    .ConfigureAwait(false);
                if (body is null || string.IsNullOrEmpty(body.AccessToken))
                {
                    return EnrolRedeemResult.Fail(EnrolRedeemErrorCode.MalformedToken, "Response missing access token.");
                }
                return EnrolRedeemResult.Ok(
                    body.AccessToken,
                    body.ExpiresIn,
                    body.DisplayName ?? "",
                    body.Email ?? "",
                    ParseMode(body.Mode));
            }

            var error = await TryReadErrorAsync(response, ct).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => EnrolRedeemResult.Fail(EnrolRedeemErrorCode.AlreadyUsed, error?.Message ?? "Already used."),
                HttpStatusCode.Gone => EnrolRedeemResult.Fail(EnrolRedeemErrorCode.Expired, error?.Message ?? "Expired."),
                _ when error?.Code is not null => EnrolRedeemResult.Fail(MapCode(error.Code), error.Message ?? error.Code),
                _ => EnrolRedeemResult.Fail(EnrolRedeemErrorCode.MalformedToken, "Redeem failed."),
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error redeeming enrol-session token");
            return EnrolRedeemResult.Fail(EnrolRedeemErrorCode.Network, "Couldn't reach Sorcha.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return EnrolRedeemResult.Fail(EnrolRedeemErrorCode.Network, "Couldn't reach Sorcha.");
        }
    }

    private static EnrolRedeemErrorCode MapCode(string code) => code switch
    {
        "malformed_token" => EnrolRedeemErrorCode.MalformedToken,
        "invalid_signature" => EnrolRedeemErrorCode.InvalidSignature,
        "scope_mismatch" => EnrolRedeemErrorCode.ScopeMismatch,
        "already_used" => EnrolRedeemErrorCode.AlreadyUsed,
        "expired" => EnrolRedeemErrorCode.Expired,
        _ => EnrolRedeemErrorCode.MalformedToken,
    };

    private static async Task<ErrorShape?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorShape>(JsonDefaults.Api, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static EnrolRedeemMode ParseMode(string? wire) => wire switch
    {
        "Standalone" or "standalone" => EnrolRedeemMode.Standalone,
        _ => EnrolRedeemMode.Gated,
    };

    private sealed record RedeemRequest(string SessionToken);
    private sealed record RedeemSuccessShape(string AccessToken, int ExpiresIn, string? DisplayName, string? Email, string? Mode);
    private sealed record ErrorShape(string? Code, string? Message);
}
