// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.Wallet.Pwa.Services.Wallet;

/// <summary>
/// HTTP implementation of <see cref="IHasWalletProbe"/>. Typed
/// <see cref="HttpClient"/> — base address is the gateway origin; the caller
/// registers the typed client with its existing bearer-token + server-clock
/// handlers.
/// </summary>
public sealed class HasWalletProbe : IHasWalletProbe
{
    private const string Endpoint = "/api/v1/wallet/exists";

    private readonly HttpClient _http;
    private readonly ILogger<HasWalletProbe> _logger;

    public HasWalletProbe(HttpClient http, ILogger<HasWalletProbe> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> HasWalletAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(Endpoint, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Non-success (incl. a 401 if the session lapsed mid-flight) —
                // fail safe: assume a wallet exists so the takeover falls through
                // to the pair flow rather than wrongly offering to create one.
                _logger.LogDebug("HasWalletProbe got {Status} — assuming wallet exists (fail-safe)", response.StatusCode);
                return true;
            }

            var payload = await response.Content.ReadFromJsonAsync<WalletExistsResponse>(JsonDefaults.Api, ct)
                .ConfigureAwait(false);
            if (payload is null)
            {
                _logger.LogWarning("HasWalletProbe received empty body — assuming wallet exists (fail-safe)");
                return true;
            }

            return payload.HasWallet;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HasWalletProbe network error — assuming wallet exists (fail-safe)");
            return true;
        }
        catch (JsonException ex)
        {
            // Empty or malformed body — ReadFromJsonAsync throws rather than
            // returning null. Fail safe to the pair flow.
            _logger.LogWarning(ex, "HasWalletProbe got a non-JSON body — assuming wallet exists (fail-safe)");
            return true;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("HasWalletProbe timed out — assuming wallet exists (fail-safe)");
            return true;
        }
    }
}
