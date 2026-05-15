// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services.User.Enrolment;

/// <summary>
/// HTTP <see cref="ITierProbeService"/> backed by two parallel calls to
/// <c>GET /api/auth/whoami</c> and <c>GET /api/me/devices</c> on the API gateway.
/// Each probe times out at 200 ms; whichever finishes first informs the result.
/// </summary>
public sealed class HttpTierProbeService : ITierProbeService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(200);
    private readonly HttpClient _http;
    private readonly ILogger<HttpTierProbeService> _logger;

    public HttpTierProbeService(HttpClient http, ILogger<HttpTierProbeService> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CitizenTier> ProbeAsync(CancellationToken ct)
    {
        var whoami = await TryWhoamiAsync(ct).ConfigureAwait(false);
        if (!whoami)
        {
            return CitizenTier.ColdStart;
        }

        var deviceCount = await TryDeviceCountAsync(ct).ConfigureAwait(false);
        return deviceCount > 0 ? CitizenTier.FastPath : CitizenTier.MiniGate;
    }

    private async Task<bool> TryWhoamiAsync(CancellationToken outer)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await _http.GetAsync("/api/auth/whoami", cts.Token).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tier probe — /api/auth/whoami failed; treating as ColdStart");
            return false;
        }
    }

    private async Task<int> TryDeviceCountAsync(CancellationToken outer)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await _http.GetAsync("/api/v1/me/devices", cts.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return 0;
            }

            var payload = await response.Content.ReadFromJsonAsync<DeviceListProbeShape>(cts.Token).ConfigureAwait(false);
            return payload?.ActiveCount ?? payload?.Devices?.Count(d => string.Equals(d.Status, "active", StringComparison.OrdinalIgnoreCase)) ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tier probe — /api/v1/me/devices failed; treating as MiniGate (best effort)");
            return 0;
        }
    }

    private sealed record DeviceListProbeShape(int? ActiveCount, IReadOnlyList<DeviceProbeRow>? Devices);
    private sealed record DeviceProbeRow(string? Status);
}
