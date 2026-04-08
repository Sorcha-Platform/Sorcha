// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default implementation of <see cref="IPersonaCryptoClient"/>. Uses a
/// typed <see cref="HttpClient"/> configured to target the Wallet Service
/// base URL with an S2S JWT attached by the existing auth pipeline.
/// </summary>
public sealed class PersonaCryptoClient : IPersonaCryptoClient
{
    private readonly HttpClient _httpClient;

    public PersonaCryptoClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<PersonaCryptoRemoteResult> EncryptAsync(
        string walletAddress,
        byte[] plaintext,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/persona/encrypt",
            new { plaintext },
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new PersonaWalletNotFoundException(walletAddress);

        if (!response.IsSuccessStatusCode)
        {
            throw new PersonaCryptoException(
                $"Persona encrypt failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var body = await response.Content.ReadFromJsonAsync<PersonaCryptoWireResponse>(ct)
            ?? throw new PersonaCryptoException("Persona encrypt returned empty body.");

        return new PersonaCryptoRemoteResult(
            body.Ciphertext ?? Array.Empty<byte>(),
            body.Nonce ?? Array.Empty<byte>(),
            body.WrappedKeyRef ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<byte[]> DecryptAsync(
        string walletAddress,
        byte[] ciphertext,
        byte[] nonce,
        string wrappedKeyRef,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/persona/decrypt",
            new { ciphertext, nonce, wrappedKeyRef },
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new PersonaWalletNotFoundException(walletAddress);

        if (!response.IsSuccessStatusCode)
        {
            throw new PersonaCryptoException(
                $"Persona decrypt failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var body = await response.Content.ReadFromJsonAsync<PersonaDecryptWireResponse>(ct)
            ?? throw new PersonaCryptoException("Persona decrypt returned empty body.");

        return body.Plaintext ?? Array.Empty<byte>();
    }

    private sealed record PersonaCryptoWireResponse(byte[]? Ciphertext, byte[]? Nonce, string? WrappedKeyRef);

    private sealed record PersonaDecryptWireResponse(byte[]? Plaintext);
}
