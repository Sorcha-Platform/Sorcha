// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models;
using Sorcha.UI.Core.Models.Authentication;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Anonymous HTTP client for the three Feature 168 social-link step-up endpoints.
/// No bearer session is attached — the link-pending token is the principal.
/// </summary>
public class AnonymousSocialLinkClientService : IAnonymousSocialLinkClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnonymousSocialLinkClientService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AnonymousSocialLinkClientService"/>.
    /// </summary>
    public AnonymousSocialLinkClientService(HttpClient httpClient, ILogger<AnonymousSocialLinkClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AnonymousLinkInitiateResult> InitiateAsync(
        string linkPendingToken,
        ChallengeMethod? preferred = null,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { linkPendingToken, preferredMethod = preferred?.ToString() };
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/auth/social/link/challenge/initiate", body, ct);

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new AnonymousLinkInitiateResult(
                    default, null, InitiateOutcome.Expired),
                HttpStatusCode.BadRequest => new AnonymousLinkInitiateResult(
                    default, null, InitiateOutcome.UnsupportedV1Method),
                HttpStatusCode.TooManyRequests => new AnonymousLinkInitiateResult(
                    default, null, InitiateOutcome.RateLimited),
                HttpStatusCode.OK => await ParseInitiateResponseAsync(response, ct),
                _ => new AnonymousLinkInitiateResult(default, null, InitiateOutcome.Failed),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling link-challenge initiate");
            return new AnonymousLinkInitiateResult(default, null, InitiateOutcome.Failed);
        }
    }

    /// <inheritdoc />
    public async Task<AnonymousLinkVerifyResult> VerifyAsync(
        string linkPendingToken,
        ChallengeMethod method,
        JsonElement proof,
        CancellationToken ct = default)
    {
        try
        {
            var body = new VerifyRequestBody(linkPendingToken, method.ToString(), proof);
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/auth/social/link/challenge/verify", body, ct);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var dto = await response.Content.ReadFromJsonAsync<VerifyResponseDto>(JsonDefaults.Api, ct);
                if (dto?.Token is null)
                    return new AnonymousLinkVerifyResult(false, null, ChallengeVerifyError.Failed);
                return new AnonymousLinkVerifyResult(true, dto.Token, ChallengeVerifyError.None);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var body401 = await TryReadErrorCodeAsync(response, ct);
                if (body401 == "proof_tier_insufficient")
                    return new AnonymousLinkVerifyResult(false, null, ChallengeVerifyError.ProofTierInsufficient);
                return new AnonymousLinkVerifyResult(false, null, ChallengeVerifyError.Failed);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var body401 = await TryReadErrorCodeAsync(response, ct);
                var error = body401 == "expired"
                    ? ChallengeVerifyError.Expired
                    : ChallengeVerifyError.ProofRejected;
                return new AnonymousLinkVerifyResult(false, null, error);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new AnonymousLinkVerifyResult(false, null, ChallengeVerifyError.Failed);

            return new AnonymousLinkVerifyResult(false, null, ChallengeVerifyError.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling link-challenge verify");
            return new AnonymousLinkVerifyResult(false, null, ChallengeVerifyError.Failed);
        }
    }

    /// <inheritdoc />
    public async Task<AnonymousLinkConfirmResult> ConfirmAsync(
        string linkPendingToken,
        string challengeToken,
        CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/social/link/confirm");
            request.Headers.Add("X-Auth-Challenge", challengeToken);
            request.Content = JsonContent.Create(new { linkPendingToken });

            using var response = await _httpClient.SendAsync(request, ct);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ParseConfirmResponseAsync(response, ct),
                HttpStatusCode.Unauthorized => new AnonymousLinkConfirmResult(
                    ConfirmOutcome.Expired, null, null, null),
                HttpStatusCode.Forbidden => new AnonymousLinkConfirmResult(
                    ConfirmOutcome.ProofInvalid, null, null, null),
                HttpStatusCode.Conflict => new AnonymousLinkConfirmResult(
                    ConfirmOutcome.Conflict, null, null, null),
                HttpStatusCode.TooManyRequests => new AnonymousLinkConfirmResult(
                    ConfirmOutcome.RateLimited, null, null, null),
                _ => new AnonymousLinkConfirmResult(ConfirmOutcome.Failed, null, null, null),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling link confirm");
            return new AnonymousLinkConfirmResult(ConfirmOutcome.Failed, null, null, null);
        }
    }

    private static async Task<AnonymousLinkInitiateResult> ParseInitiateResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var dto = await response.Content.ReadFromJsonAsync<InitiateResponseDto>(JsonDefaults.Api, ct);
        if (dto is null)
            return new AnonymousLinkInitiateResult(default, null, InitiateOutcome.Failed);

        if (!Enum.TryParse<ChallengeMethod>(dto.Method, ignoreCase: true, out var method))
            return new AnonymousLinkInitiateResult(default, null, InitiateOutcome.UnsupportedV1Method);

        return new AnonymousLinkInitiateResult(method, dto.Payload, InitiateOutcome.Ok);
    }

    private static async Task<AnonymousLinkConfirmResult> ParseConfirmResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var dto = await response.Content.ReadFromJsonAsync<ConfirmResponseDto>(JsonDefaults.Api, ct);
        if (dto?.AccessToken is null)
            return new AnonymousLinkConfirmResult(ConfirmOutcome.Failed, null, null, null);
        return new AnonymousLinkConfirmResult(ConfirmOutcome.Linked, dto.AccessToken, dto.RefreshToken, dto.ExpiresIn);
    }

    private static async Task<string?> TryReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var dto = await response.Content.ReadFromJsonAsync<ErrorDto>(JsonDefaults.Api, ct);
            return dto?.Code?.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    // Internal DTOs for wire deserialization only

    private sealed record InitiateResponseDto(string? Method, JsonElement? Payload);

    private sealed record VerifyResponseDto(string? Token, int? ExpiresIn);

    private sealed record ConfirmResponseDto(string? AccessToken, string? RefreshToken, int? ExpiresIn);

    private sealed record ErrorDto(string? Code);

    private sealed record VerifyRequestBody(
        string LinkPendingToken,
        string Method,
        JsonElement Proof);
}
