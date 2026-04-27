// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Typed client for the Tenant Service auth-method endpoints (Feature 116).
/// US4 ships only the aggregate read; mutation methods (link / unlink / set
/// / change / remove / rename) get added in US1, US2, and US3 respectively.
/// </summary>
public interface IAuthMethodsClientService
{
    /// <summary>
    /// Fetch the signed-in user's sign-in methods in a single round-trip.
    /// Returns null on transport failure or 404 (caller renders an error
    /// state rather than throwing).
    /// </summary>
    Task<AuthMethodsResponse?> GetAuthMethodsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IAuthMethodsClientService"/> implementation.
/// </summary>
public sealed class AuthMethodsClientService : IAuthMethodsClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthMethodsClientService> _logger;

    /// <summary>Creates a new <see cref="AuthMethodsClientService"/>.</summary>
    public AuthMethodsClientService(HttpClient httpClient, ILogger<AuthMethodsClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthMethodsResponse?> GetAuthMethodsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AuthMethodsResponse>(
                "/api/me/auth-methods", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch /api/me/auth-methods");
            return null;
        }
    }
}
