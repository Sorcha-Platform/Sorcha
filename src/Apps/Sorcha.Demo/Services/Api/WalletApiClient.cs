// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using System.Text.Json;
using Sorcha.Wallet.Contracts.Models;

namespace Sorcha.Demo.Services.Api;

/// <summary>
/// Client for Wallet Service API.
///
/// <para>Issue #1158 — this client used to declare its own <c>CreateWalletResponse</c>,
/// <c>WalletDetails</c>, <c>WalletResponse</c> and <c>SignatureResponse</c>, and was the sole entry
/// in <c>.wallet-contracts-allowlist</c>. It now binds the canonical
/// <see cref="Sorcha.Wallet.Contracts.Models"/> shapes, so the ratchet is empty.</para>
///
/// <para>The bespoke <c>WalletResponse</c> was not merely duplicative, it was wrong on the wire for
/// two of the four calls it served: <c>GetWalletAsync</c> and <c>ListWalletsAsync</c> deserialised
/// the server's <see cref="WalletDto"/> into a type carrying <c>Mnemonic</c> and
/// <c>AccountIndex</c> (which no read endpoint returns, so they were always null) while silently
/// dropping <c>Status</c>, <c>Owner</c>, <c>Tenant</c> and <c>UpdatedAt</c> (which it does).</para>
/// </summary>
public class WalletApiClient : ApiClientBase
{
    private readonly string _baseUrl;

    public WalletApiClient(HttpClient httpClient, ILogger<WalletApiClient> logger, string baseUrl)
        : base(httpClient, logger)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Creates a new HD wallet
    /// </summary>
    public async Task<CreateWalletResponse?> CreateWalletAsync(
        string name,
        string algorithm = "ED25519",
        CancellationToken ct = default)
    {
        var request = new
        {
            name,
            algorithm
        };

        // Returns the canonical nested shape: { wallet: WalletDto, mnemonicWords: string[], … }.
        // Previously flattened into a bespoke WalletResponse "for backward compatibility" with a
        // caller that no longer exists — the one consumer reads the nested shape directly now.
        return await PostAsync<object, CreateWalletResponse>($"{_baseUrl}/wallets", request, ct);
    }

    /// <summary>
    /// Gets wallet details by address
    /// </summary>
    public async Task<WalletDto?> GetWalletAsync(string address, CancellationToken ct = default)
    {
        return await GetAsync<WalletDto>($"{_baseUrl}/wallets/{address}", ct);
    }

    /// <summary>
    /// Lists all wallets
    /// </summary>
    public async Task<List<WalletDto>?> ListWalletsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<WalletDto>>($"{_baseUrl}/wallets", ct);
    }

    /// <summary>
    /// Signs data with a wallet
    /// </summary>
    public async Task<SignTransactionResponse?> SignAsync(
        string walletAddress,
        string dataToSign,
        CancellationToken ct = default)
    {
        var request = new
        {
            data = dataToSign
        };

        return await PostAsync<object, SignTransactionResponse>(
            $"{_baseUrl}/wallets/{walletAddress}/sign",
            request,
            ct);
    }

    /// <summary>
    /// Derives a child wallet (for delegation scenarios)
    /// </summary>
    public async Task<WalletDto?> DeriveChildWalletAsync(
        string parentAddress,
        uint childIndex,
        string? name = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            childIndex,
            name = name ?? $"Child-{childIndex}"
        };

        return await PostAsync<object, WalletDto>(
            $"{_baseUrl}/wallets/{parentAddress}/derive",
            request,
            ct);
    }

    /// <summary>
    /// Checks Wallet Service health
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        return await base.CheckHealthAsync(_baseUrl, ct);
    }
}
