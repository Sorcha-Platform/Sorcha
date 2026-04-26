// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// PWA-side authentication facade (Feature 114, T109 foundation). Wraps the
/// existing Tenant Service <c>POST /api/auth/login</c> surface so the wallet
/// can obtain a citizen JWT, persist it via <see cref="IAccessTokenStore"/>,
/// and surface a clean signed-in / signed-out signal to the UI.
/// </summary>
public interface IAuthService
{
    /// <summary>True if the wallet currently holds a non-expired access token.</summary>
    Task<bool> IsSignedInAsync(CancellationToken ct = default);

    /// <summary>The signed-in citizen's email if known, else null.</summary>
    Task<string?> GetSignedInEmailAsync(CancellationToken ct = default);

    /// <summary>
    /// Sign in with email + password. Persists the access token on success.
    /// Returns a result describing the outcome (success, invalid credentials,
    /// 2FA required — 2FA flow lands in a follow-up).
    /// </summary>
    Task<SignInResult> SignInAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Clears the persisted token.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IAuthService.SignInAsync"/>.</summary>
public sealed record SignInResult(SignInStatus Status, string? ErrorMessage = null)
{
    /// <summary>Convenience for happy-path checks.</summary>
    public bool IsSuccess => Status == SignInStatus.Success;
}

/// <summary>Possible sign-in outcomes.</summary>
public enum SignInStatus
{
    /// <summary>Sign-in completed; token persisted.</summary>
    Success = 0,
    /// <summary>Server rejected the credentials.</summary>
    InvalidCredentials = 1,
    /// <summary>Two-factor required — handled in a follow-up wave.</summary>
    TwoFactorRequired = 2,
    /// <summary>Network or server error.</summary>
    ServerError = 3,
}

/// <summary>
/// Default <see cref="IAuthService"/>. Calls <c>POST /api/auth/login</c> via
/// the Tenant Service through the API Gateway. Uses a dedicated unauthenticated
/// HttpClient (no <see cref="BearerTokenHandler"/>) to avoid sending stale
/// tokens with sign-in requests.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly IAccessTokenStore _store;

    /// <summary>Initialises a new instance.</summary>
    public AuthService(HttpClient http, IAccessTokenStore store)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<bool> IsSignedInAsync(CancellationToken ct = default)
        => await _store.GetAsync(ct) is not null;

    /// <inheritdoc />
    public async Task<string?> GetSignedInEmailAsync(CancellationToken ct = default)
        => (await _store.GetAsync(ct))?.Email;

    /// <inheritdoc />
    public async Task<SignInResult> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequest(email.Trim(), password),
                ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new SignInResult(SignInStatus.InvalidCredentials, "Invalid email or password.");
            }
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
            if (body is null) return new SignInResult(SignInStatus.ServerError, "Empty response from auth server.");

            if (body.RequiresTwoFactor)
            {
                return new SignInResult(SignInStatus.TwoFactorRequired,
                    "Two-factor authentication is required — full 2FA flow lands in a follow-up wave.");
            }

            if (string.IsNullOrEmpty(body.AccessToken))
            {
                return new SignInResult(SignInStatus.ServerError, "Auth server did not return an access token.");
            }

            var record = new AccessTokenRecord(
                body.AccessToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, body.ExpiresIn)),
                email.Trim());
            await _store.SetAsync(record, ct);
            return new SignInResult(SignInStatus.Success);
        }
        catch (HttpRequestException ex)
        {
            return new SignInResult(SignInStatus.ServerError, ex.Message);
        }
    }

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken ct = default) => _store.ClearAsync(ct);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record LoginResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("requires_two_factor")] bool RequiresTwoFactor);
}
