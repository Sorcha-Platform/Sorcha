// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.ServiceClients.CitizenWallet;

/// <summary>
/// Default <see cref="ICitizenWalletClient"/>. Targets the Wallet Service via
/// the configured <c>ServiceClients:WalletService:Address</c> (Aspire service
/// discovery resolves this in process; production deployments override).
/// </summary>
/// <remarks>
/// This client does NOT inject a service principal token — citizen-wallet
/// endpoints require a citizen JWT (audience <c>sorcha:citizen-wallet</c>) that
/// the PWA acquires through the existing Sorcha auth flow. Caller is responsible
/// for setting <see cref="HttpClient.DefaultRequestHeaders"/>.Authorization or
/// using a <see cref="DelegatingHandler"/> that does so.
/// </remarks>
public sealed class CitizenWalletClient : ICitizenWalletClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CitizenWalletClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CitizenWalletClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CitizenWalletClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = configuration["ServiceClients:WalletService:Address"]
            ?? "https+http://wallet-service";

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<DeviceEnrolmentResponse> EnrolDeviceAsync(
        DeviceEnrolmentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _httpClient.PostAsJsonAsync(
            "api/v1/wallet/devices/enrol", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DeviceEnrolmentResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException(
            "Wallet Service returned an empty body for /devices/enrol.");
    }

    /// <inheritdoc />
    public async Task<SyncResponse?> SyncAsync(string? sinceToken, CancellationToken ct = default)
    {
        var path = string.IsNullOrEmpty(sinceToken)
            ? "api/v1/wallet/sync"
            : $"api/v1/wallet/sync?since={Uri.EscapeDataString(sinceToken)}";

        var response = await _httpClient.GetAsync(path, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Gone) return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SyncResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Wallet Service returned an empty body for /sync.");
    }

    /// <inheritdoc />
    public async Task<CredentialListResponse> ListCredentialsAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("api/v1/wallet/credentials", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CredentialListResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Wallet Service returned an empty body for /credentials.");
    }

    /// <inheritdoc />
    public async Task<DelegationRenewalResponse?> RenewDelegationAsync(
        Guid deviceId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/v1/wallet/devices/renew-delegation",
            new DelegationRenewalRequest { DeviceId = deviceId },
            JsonOptions, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<DelegationRenewalResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Wallet Service returned an empty body for /devices/renew-delegation.");
    }

    /// <inheritdoc />
    public async Task<DeviceListResponse> ListDevicesAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("api/v1/wallet/devices", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<DeviceListResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Wallet Service returned an empty body for /devices.");
    }

    /// <inheritdoc />
    public async Task<bool> RenameDeviceAsync(
        Guid deviceId, string label, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/v1/wallet/devices/{deviceId}/label",
            new DeviceLabelUpdateRequest { Label = label },
            JsonOptions, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeDeviceAsync(
        Guid deviceId, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"api/v1/wallet/devices/{deviceId}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ReportPresentationLogAsync(
        PresentationLogReportRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _httpClient.PostAsJsonAsync(
            "api/v1/wallet/presentations/log", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return response.StatusCode == System.Net.HttpStatusCode.Accepted;
    }
}
