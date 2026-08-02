// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Serialization;

namespace Sorcha.UI.Core.Services.User.Devices;

/// <summary>
/// Issue #1311 — the citizen's enrolled-device list and revoke, over a typed client the host wires
/// with <c>AuthenticatedHttpMessageHandler</c>.
///
/// <para><b>Why this type exists.</b> <c>MyDevices.razor</c> called
/// <c>GET /api/v1/me/devices</c> and <c>DELETE /api/v1/me/devices/{id}</c> on the ambient
/// <c>@inject HttpClient</c>, which carries NO <c>Authorization</c> header by design (it is the
/// client the auth service itself uses; wiring the handler into it would be circular DI). Both
/// endpoints are authenticated, so every request 401'd. The page caught the exception and rendered
/// <i>"Could not load devices. Check your connection and try again."</i> — blaming the network for an
/// auth failure, on a page that could never have worked for anyone.</para>
///
/// <para>This is the third incident of that class (after #1165/#1166 device pairing and #1167 the
/// F181 admin clients), which is why #1311 adds a ratchet gate alongside the fix rather than just
/// correcting the call site.</para>
/// </summary>
public interface IMyDevicesClient
{
    /// <summary>
    /// The caller's enrolled devices, or <see langword="null"/> when the request failed — the caller
    /// renders an error rather than an empty list, because "no devices" and "couldn't ask" are
    /// different things to a citizen deciding whether to pair a phone.
    /// </summary>
    Task<IReadOnlyList<DeviceSummary>?> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes a device. See <see cref="DeviceRevokeOutcome"/> for the outcomes.</summary>
    Task<DeviceRevokeOutcome> RevokeAsync(Guid deviceId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a device revoke (issue #1311).</summary>
public enum DeviceRevokeOutcome
{
    /// <summary>Server confirmed (204).</summary>
    Revoked = 0,

    /// <summary>
    /// Server returned 404. Deliberately indistinguishable from a cross-user probe server-side, so
    /// the caller must treat it as "already gone", never as "belongs to someone else".
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// Server returned 502: the Tenant row IS revoked but the Wallet service-to-service status-list
    /// flip failed. Distinct from <see cref="Failed"/> because the revoke partially succeeded — the
    /// caller must say so and invite a retry to complete it, not report a flat failure.
    /// </summary>
    StatusListFlipFailed = 2,

    /// <summary>Transport failure or unexpected status.</summary>
    Failed = 3,
}

/// <inheritdoc />
public sealed class MyDevicesClient : IMyDevicesClient
{
    private const string Endpoint = "/api/v1/me/devices";

    private readonly HttpClient _httpClient;
    private readonly ILogger<MyDevicesClient> _logger;

    public MyDevicesClient(HttpClient httpClient, ILogger<MyDevicesClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceSummary>?> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient
                .GetFromJsonAsync<DeviceListResponse>(Endpoint, SorchaJson.Options, cancellationToken)
                .ConfigureAwait(false);

            return response?.Devices ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Endpoint}", Endpoint);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<DeviceRevokeOutcome> RevokeAsync(
        Guid deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .DeleteAsync($"{Endpoint}/{deviceId}", cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => DeviceRevokeOutcome.Revoked,
                HttpStatusCode.NotFound => DeviceRevokeOutcome.NotFound,
                HttpStatusCode.BadGateway => DeviceRevokeOutcome.StatusListFlipFailed,
                _ => DeviceRevokeOutcome.Failed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke device {DeviceId}", deviceId);
            return DeviceRevokeOutcome.Failed;
        }
    }
}
