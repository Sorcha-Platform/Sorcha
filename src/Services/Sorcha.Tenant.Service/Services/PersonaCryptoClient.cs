// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default implementation of <see cref="IPersonaCryptoClient"/>. Uses a
/// typed <see cref="HttpClient"/> configured to target the Wallet Service
/// base URL. Attaches a service-to-service JWT (carrying the
/// <c>persona:crypto</c> scope) to every request so the Wallet Service's
/// <c>RequirePersonaCrypto</c> authorization policy accepts the call.
/// </summary>
public sealed class PersonaCryptoClient : IPersonaCryptoClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<PersonaCryptoClient> _logger;

    public PersonaCryptoClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        ILogger<PersonaCryptoClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PersonaCryptoRemoteResult> EncryptAsync(
        string walletAddress,
        byte[] plaintext,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/persona/encrypt")
        {
            Content = JsonContent.Create(new PersonaEncryptRequest(plaintext))
        };
        await AttachServiceTokenAsync(request, ct);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new PersonaWalletNotFoundException(walletAddress);

        if (!response.IsSuccessStatusCode)
        {
            throw new PersonaCryptoException(
                $"Persona encrypt failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var body = await response.Content.ReadFromJsonAsync<PersonaCryptoWireResponse>(ct)
            ?? throw new PersonaCryptoException("Persona encrypt returned empty body.");

        // A 200 response with null crypto fields is a protocol violation —
        // failing here is strictly better than silently storing empty bytes
        // which would break decrypt on the next read.
        if (body.Ciphertext is null || body.Ciphertext.Length == 0
            || body.Nonce is null || body.Nonce.Length == 0
            || string.IsNullOrEmpty(body.WrappedKeyRef))
        {
            throw new PersonaCryptoException(
                "Persona encrypt response is missing required fields (ciphertext, nonce, or wrappedKeyRef).");
        }

        return new PersonaCryptoRemoteResult(body.Ciphertext, body.Nonce, body.WrappedKeyRef);
    }

    /// <inheritdoc />
    public async Task<byte[]> DecryptAsync(
        string walletAddress,
        byte[] ciphertext,
        byte[] nonce,
        string wrappedKeyRef,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/persona/decrypt")
        {
            Content = JsonContent.Create(new PersonaDecryptRequest(ciphertext, nonce, wrappedKeyRef))
        };
        await AttachServiceTokenAsync(request, ct);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new PersonaWalletNotFoundException(walletAddress);

        if (!response.IsSuccessStatusCode)
        {
            throw new PersonaCryptoException(
                $"Persona decrypt failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var body = await response.Content.ReadFromJsonAsync<PersonaDecryptWireResponse>(ct)
            ?? throw new PersonaCryptoException("Persona decrypt returned empty body.");

        if (body.Plaintext is null)
        {
            throw new PersonaCryptoException("Persona decrypt response is missing the plaintext field.");
        }

        return body.Plaintext;
    }

    /// <summary>
    /// Fetches the current service token from <see cref="IServiceAuthClient"/>
    /// and attaches it as a Bearer header on the outgoing request. The
    /// Tenant Service client credentials must be configured to include the
    /// <c>persona:crypto</c> scope; the Wallet Service's
    /// <c>RequirePersonaCrypto</c> policy enforces that scope is present.
    /// </summary>
    /// <summary>
    /// Fails fast when no service token is available rather than sending an
    /// unauthenticated request — the Wallet Service would reject it with a
    /// 401/403 and the caller would see an opaque <see cref="PersonaCryptoException"/>
    /// instead of a clear configuration-error message.
    /// </summary>
    private async Task AttachServiceTokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _serviceAuth.GetTokenAsync(ct);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("No service token available when calling Wallet persona crypto endpoints");
            throw new InvalidOperationException(
                "Service token unavailable — cannot call persona crypto endpoint. " +
                "Check the Tenant Service client credentials and that the persona:crypto scope is granted.");
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    // Typed request DTOs — mirror the Wallet Service's request records
    // (PersonaEncryptRequest / PersonaDecryptRequest). Using records rather
    // than anonymous objects keeps the contract explicit on both sides.
    private sealed record PersonaEncryptRequest(byte[] Plaintext);
    private sealed record PersonaDecryptRequest(byte[] Ciphertext, byte[] Nonce, string WrappedKeyRef);

    private sealed record PersonaCryptoWireResponse(byte[]? Ciphertext, byte[]? Nonce, string? WrappedKeyRef);
    private sealed record PersonaDecryptWireResponse(byte[]? Plaintext);
}
