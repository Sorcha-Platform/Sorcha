// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.UI.Core.Models.Wallet;

namespace Sorcha.UI.Core.Services.Wallet;

/// <summary>
/// HTTP client implementation for the Wallet API
/// </summary>
public class WalletApiService : IWalletApiService
{
    private readonly HttpClient _httpClient;
    private const string BasePath = "/api/v1/wallets";

    public WalletApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<List<WalletDto>> GetWalletsAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(BasePath, ct);
        response.EnsureSuccessStatusCode();

        var wallets = await response.Content.ReadFromJsonAsync<List<WalletDto>>(ct);
        return wallets ?? new List<WalletDto>();
    }

    /// <inheritdoc />
    public async Task<WalletDto?> GetWalletAsync(string address, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"{BasePath}/{address}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WalletDto>(ct);
    }

    /// <inheritdoc />
    public async Task<CreateWalletResponse> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(BasePath, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateWalletResponse>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize wallet creation response");
    }

    /// <inheritdoc />
    public async Task<WalletDto> RecoverWalletAsync(RecoverWalletRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BasePath}/recover", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WalletDto>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize wallet recovery response");
    }

    /// <inheritdoc />
    public async Task<bool> DeleteWalletAsync(string address, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"{BasePath}/{address}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<SignTransactionResponse> SignDataAsync(string address, SignTransactionRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BasePath}/{address}/sign", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignTransactionResponse>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize signing response");
    }

    /// <inheritdoc />
    public async Task<AddressListResponse> GetAddressesAsync(string address, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"{BasePath}/{address}/addresses?page={page}&pageSize={pageSize}", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AddressListResponse>(ct);
        return result ?? new AddressListResponse { WalletAddress = address };
    }

    /// <inheritdoc />
    public async Task<WalletAddressDto> RegisterAddressAsync(string address, RegisterAddressRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BasePath}/{address}/addresses", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WalletAddressDto>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize address registration response");
    }
}
