// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>Reads the configured social providers so the sign-in screen renders only enabled buttons.</summary>
public interface ISocialProvidersClient
{
    /// <summary>Provider names enabled on this host; empty on failure.</summary>
    Task<IReadOnlyList<string>> GetConfiguredAsync(CancellationToken ct = default);
}

/// <summary>HTTP <see cref="ISocialProvidersClient"/> over the anonymous providers endpoint.</summary>
public sealed class SocialProvidersClient : ISocialProvidersClient
{
    private readonly HttpClient _http;

    /// <summary>Initialises a new instance.</summary>
    public SocialProvidersClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetConfiguredAsync(CancellationToken ct = default)
    {
        try
        {
            var body = await _http.GetFromJsonAsync<ProvidersBody>("api/auth/social/providers", ct);
            return body?.Providers ?? [];
        }
        catch { return []; }
    }

    private sealed record ProvidersBody([property: JsonPropertyName("providers")] IReadOnlyList<string>? Providers);
}
