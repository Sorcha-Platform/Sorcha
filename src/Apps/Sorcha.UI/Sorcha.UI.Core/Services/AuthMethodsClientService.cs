// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Typed client for the Tenant Service auth-method endpoints (Feature 116).
/// US4 shipped the aggregate read; US1 adds social link/unlink. US2 (passkey
/// rename + soft-revoke) and US3 (password set/change/remove) wire onto this
/// same client in their PRs.
/// </summary>
public interface IAuthMethodsClientService
{
    /// <summary>
    /// Fetch the signed-in user's sign-in methods in a single round-trip.
    /// Returns null on transport failure or 404 (caller renders an error
    /// state rather than throwing).
    /// </summary>
    Task<AuthMethodsResponse?> GetAuthMethodsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a social-provider link flow — POSTs to <c>/api/auth/social/initiate</c>
    /// with <c>intent=link</c>. The signed-in caller's PlatformUserId is captured
    /// server-side into the cached state. Returns the OAuth authorization URL the
    /// browser should navigate to. Null on transport failure.
    /// </summary>
    /// <param name="provider">Provider name: <c>google</c>, <c>github</c>, <c>microsoft</c>, or <c>apple</c>.</param>
    Task<string?> InitiateSocialLinkAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlink a social provider from the signed-in PlatformUser. The caller MUST
    /// have completed a fresh re-authentication challenge and present the resulting
    /// opaque token in <paramref name="challengeToken"/>; the server-side filter
    /// rejects calls without it.
    /// </summary>
    Task<UnlinkSocialOutcome> UnlinkSocialAsync(
        Guid linkId, string challengeToken, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of an unlink call surfaced to the UI.</summary>
public enum UnlinkSocialOutcome
{
    /// <summary>Server confirmed the row was hard-deleted.</summary>
    Unlinked = 0,

    /// <summary>Last-method-floor protection refused the removal.</summary>
    LastSignInMethodProtected = 1,

    /// <summary>Server returned 401 — usually a stale or missing challenge token.</summary>
    Forbidden = 2,

    /// <summary>Server returned 404 — link not found or owned by another user.</summary>
    NotFound = 3,

    /// <summary>Transport failure or unexpected status code.</summary>
    Failed = 4,
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

    /// <inheritdoc />
    public async Task<string?> InitiateSocialLinkAsync(string provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/auth/social/initiate",
                new { provider, intent = "link" },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Social link initiate returned {StatusCode} for {Provider}",
                    response.StatusCode, provider);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SocialLinkInitiateResponse>(
                cancellationToken: cancellationToken);
            return payload?.AuthorizationUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate social link for {Provider}", provider);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UnlinkSocialOutcome> UnlinkSocialAsync(
        Guid linkId, string challengeToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(challengeToken);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/social/{linkId:D}");
            request.Headers.Add("X-Auth-Challenge", challengeToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.NoContent => UnlinkSocialOutcome.Unlinked,
                System.Net.HttpStatusCode.Conflict => UnlinkSocialOutcome.LastSignInMethodProtected,
                System.Net.HttpStatusCode.Unauthorized => UnlinkSocialOutcome.Forbidden,
                System.Net.HttpStatusCode.NotFound => UnlinkSocialOutcome.NotFound,
                _ => UnlinkSocialOutcome.Failed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unlink social {LinkId} failed", linkId);
            return UnlinkSocialOutcome.Failed;
        }
    }

    private sealed record SocialLinkInitiateResponse
    {
        public string AuthorizationUrl { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
    }
}
